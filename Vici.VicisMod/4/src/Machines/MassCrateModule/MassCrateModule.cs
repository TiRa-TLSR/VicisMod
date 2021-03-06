﻿using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

public abstract class MassCrateModule : MachineEntity {

    public const string CUBE_NAME = "Vici.MassCrateModule";

    public MassCrateModuleManager manager;
    public MassGiver giver;
    public MassTaker taker;
    public List<MassCrateModule> neighbors = new List<MassCrateModule>();
    public bool ping = false;
    private bool encounteredNullSegment = false;
    protected Color cubeColor;
    protected GameObject gameObject;
    protected bool linkedToGo;

    protected int maxBinSize = 0;
    protected int maxBins = 0;
    protected int maxItems = 0;

    protected List<ItemBase> items;

    public MassCrateModule(ModCreateSegmentEntityParameters parameters) :
         base(eSegmentEntity.Mod,
            SpawnableObjectEnum.MassStorageCrate,
            parameters.X,
            parameters.Y,
            parameters.Z,
            parameters.Cube,
            parameters.Flags,
            parameters.Value,
            parameters.Position,
            parameters.Segment) {
        VicisMod.log(getPrefix(), "Being created at [" + parameters.X + ", " + parameters.Y + ", " + parameters.Z + "], loaded from disk = " + parameters.LoadFromDisk);
        mbNeedsLowFrequencyUpdate = true;
        mbNeedsUnityUpdate = true;
        cubeColor = Color.white;
        LookForAttachedModules();
        items = new List<ItemBase>();
    }

    public abstract string getPrefix();

    public int getNumItems() { return ItemBaseUtil.getItemCount(items); }
    public int getMaxItems() { return maxItems; }
    public int getNumBins() { return items.Count; }
    public int getMaxBins() { return maxBins; }
    public int getMaxBinSize() { return maxBinSize; }

    public override void UnitySuspended() {
        gameObject = null;
    }

    public override void DropGameObject() {
        base.DropGameObject();
        linkedToGo = false;
    }

    public override void UnityUpdate() {
        if (!linkedToGo) {
            if (mWrapper != null && mWrapper.mGameObjectList != null) {
                gameObject = mWrapper.mGameObjectList[0].gameObject;
                GameObject plate = gameObject.transform.Search("Docking_Port").gameObject;
                plate.SetActive(false);
                GameObject crate = gameObject.transform.Search("StorageCratePivot").gameObject;
                MeshRenderer[] components = crate.GetComponentsInChildren<MeshRenderer>();
                if (components != null && components.Length > 0) {
                    foreach (MeshRenderer mesh in components) {
                        mesh.material.SetColor("_Color", cubeColor);
                    }
                } else {
                    VicisMod.log(getPrefix(), "Could not establish MeshRenderers for gameobject!");
                }
                linkedToGo = true;
            }
        }
    }

    protected virtual void LookForAttachedModules() {
        VicisMod.log(getPrefix(), "Looking neigbors");
        
        List<MassCrateModule> list = VicisMod.checkSurrounding<MassCrateModule>(this, out encounteredNullSegment);

        foreach(MassCrateModule m in list) {
            if(manager == null) {
                m.manager.Add(this);
            } else if (manager != m.manager) {
                manager.Merge(m.manager);
            }
            m.AddNeighbor(this);
            AddNeighbor(m);
        }

        VicisMod.log(getPrefix(), "I now have " + neighbors.Count + " neighbors");

        // FIRST!
        if(manager == null) {
            VicisMod.log(getPrefix(), "Creating a new manager for myself");
            MassCrateModuleManager newManager = new MassCrateModuleManager();
            newManager.Add(this);
        }
    }

    public override void LowFrequencyUpdate() {
        // If we somehow lost our manager (how?) or we found a null segment last time,
        // re-check for neighbors
        if(manager == null || encounteredNullSegment) {
            VicisMod.log(getPrefix(), "looking for neighbors again. manager == null => " + (manager == null) + ", encounteredNullSegment = " + encounteredNullSegment);
            LookForAttachedModules();
        }
    }

    public void AddNeighbor(MassCrateModule mcm) {
        if (neighbors.Contains(mcm)) return;
        neighbors.Add(mcm);
    }

    public void RemoveNeighbor(MassCrateModule mcm) {
        neighbors.Remove(mcm);
    }

    public override void OnDelete() {
        base.OnDelete();
        // Can't let my neighbors know of me during the flood fill
        foreach (MassCrateModule mcm in neighbors) {
            mcm.RemoveNeighbor(this);
        }
        manager.Remove(this);
        neighbors.Clear();

        if (giver != null) giver.mcm = null;
        if (taker != null) taker.mcm = null;

        foreach(ItemBase it in items) {
            ItemManager.instance.DropItem(it, mnX, mnY, mnZ, Vector3.zero);
        }
    }

    // An item that we'll try taking, and a flag whether we should actually take it or not (we could just be checking if we have space)
    public bool AttemptGiveItem(ItemBase item, int amount = 1, bool actuallyTakeItem = true) {
        VicisMod.log(getPrefix(), "Attempting to receive " + amount + " item " + item.GetDisplayString() + " with id = " + item.mnItemID + " and type = " + item.mType);
        // Can we even take this item(s)?
        if (getNumItems() + amount > maxItems) return false;
        for(int i = 0; i < items.Count; ++i) {
            if(ItemBaseUtil.compareCubeStack(items[i] as ItemCubeStack, item as ItemCubeStack)) {
                ItemCubeStack a = items[i] as ItemCubeStack;
                int amntTaken = Math.Min(amount, a.mnAmount);
                if (a != null && a.mnAmount < maxBinSize) {
                    if (actuallyTakeItem) {
                        a.mnAmount += amount;
                    }
                    MarkDirtyDelayed();
                    return true;
                } 
            } else if (ItemBaseUtil.compareStack(items[i] as ItemStack, item as ItemStack)) {
                ItemStack a = items[i] as ItemStack;
                if (a != null && a.mnAmount < maxBinSize) {
                    if (actuallyTakeItem) {
                        a.mnAmount += amount;
                    }
                    MarkDirtyDelayed();
                    return true;
                }
            }
        }

        if(items.Count < maxBins) {
            if (actuallyTakeItem) {
                items.Add(item);
            }
            MarkDirtyDelayed();
            return true;
        }

        VicisMod.log(getPrefix(), "Did not accept item " + item.GetDisplayString());
        return false;
    }

    // An item that we're going to give, or check that we can give
    public ItemBase AttemptTakeItem(ItemBase item, int amount = 1, bool actuallyGiveItem = true) {
        VicisMod.log(getPrefix(), "Attempting to give " + amount + " item " + item.GetDisplayString() + " with id = " + item.mnItemID + " and type = " + item.mType);
        if (getNumItems() == 0) return null;
        for(int i = 0; i < items.Count; ++i) {
            if(ItemBaseUtil.compareCubeStack(items[i] as ItemCubeStack, item as ItemCubeStack)) {
                ItemCubeStack a = items[i] as ItemCubeStack;
                VicisMod.log(getPrefix(), "Found a CubeStack " + a.GetDisplayString() + ", which is storing " + a.mnAmount + " blocks");
                if(a != null) {
                    int amntTaken = Math.Min(amount, a.mnAmount);
                    ItemCubeStack ret = new ItemCubeStack(a.mCubeType, a.mCubeValue, amntTaken);
                    if(actuallyGiveItem) {
                        VicisMod.log(getPrefix(), "Taking away");
                        a.mnAmount -= amntTaken;
                        if (a.mnAmount <= 0) {
                            VicisMod.log(getPrefix(), "There are " + a.mnAmount + " items for " + a.GetDisplayString() + ", removing it from items");
                            items.RemoveAt(i);
                        }
                    }
                    MarkDirtyDelayed();
                    return ret;
                }
                // a was null, this is bad. Clean up, hide the evidence
                VicisMod.log(getPrefix(), "Removing a null that masquaraded as an ItemCubeStack!");
                items.RemoveAt(i);
                return null;
            } else if (ItemBaseUtil.compareStack(items[i] as ItemStack, item as ItemStack)) {
                ItemStack a = items[i] as ItemStack;
                VicisMod.log(getPrefix(), "Found a Stack " + a.GetDisplayString() + ", which is storing " + a.mnAmount + " blocks");
                if (a != null) {
                    int amntTaken = Math.Min(amount, a.mnAmount);
                    ItemStack ret = new ItemStack(a.mnItemID, amntTaken);
                    if (actuallyGiveItem) {
                        VicisMod.log(getPrefix(), "Taking away");
                        a.mnAmount -= amntTaken;
                        if (a.mnAmount <= 0) {
                            VicisMod.log(getPrefix(), "There are " + a.mnAmount + " items for " + a.GetDisplayString() + ", removing it from items");
                            items.RemoveAt(i);
                        }
                    }
                    MarkDirtyDelayed();
                    return ret;
                }
                // a was null, this is bad. Clean up, hide the evidence
                VicisMod.log(getPrefix(), "Removing a null that masquaraded as an ItemStack!");
                items.RemoveAt(i);
                return null;
            }
        }

        if (ItemBaseUtil.isStack(item)) {
            VicisMod.log(getPrefix(), "Could not find a stack or cube stack for " + item.GetDisplayString() + ", returning null");
            return null;
        }

        // What we're looking for is not a stack, start looking at the individual items.
        for (int i = 0; i < items.Count; ++i) {
            if(ItemBaseUtil.compareBase(items[i], item)) {
                ItemBase ret = items[i];
                VicisMod.log(getPrefix(), "Found a " + ret.GetDisplayString() + ", with id = " + ret.mnItemID + " and type = " + ret.mType);
                if(actuallyGiveItem) {
                    VicisMod.log(getPrefix(), "Removing from items");
                    items.RemoveAt(i);
                }
                MarkDirtyDelayed();
                return ret;
            }
        }
        return null;
    }

    public override string GetPopupText() {
        string ret = "I'm connected to " + manager.modules.Keys.Count + " modules" +
            "\nNetwork storing " + manager.getNumItems() + " / " + manager.getMaxItems() + " items";
        if (maxItems > 0) {
            ret += "\nThis crate storing " + getNumItems() + " / " + maxItems + " items";
        }

        foreach (ItemBase it in items) {
            ret += "\n" + it.GetDisplayString();
        }
        
        return ret;
    }

    public override void Write(BinaryWriter writer) {
        VicisMod.log(getPrefix(), "Currently holding: " + GetPopupText());
        writer.Write(items.Count);
        for(int i = 0; i < items.Count; ++i) {
            VicisMod.log(getPrefix(), "Writing to file " + items[i].GetDisplayString());
            ItemFile.SerialiseItem(items[i], writer);
        }
    }

    public override void Read(BinaryReader reader, int entityVersion) {
        VicisMod.VicisModVersion version = (VicisMod.VicisModVersion)entityVersion;
        items.Clear();
        switch (version) {
            case VicisMod.VicisModVersion.Version1:
            case VicisMod.VicisModVersion.Version2:
            case VicisMod.VicisModVersion.Version3:
                break;
            default:
                int count = reader.ReadInt32();
                for(int i = 0; i < count; ++i) {
                    ItemBase item = ItemFile.DeserialiseItem(reader);
                    VicisMod.log(getPrefix(), "Reading from file " + item.GetDisplayString());
                    items.Add(item);
                }
                break;
        }
    }

    public override bool ShouldSave() {
        return true;
    }

    public override int GetVersion() {
        return (int)VicisMod.VicisModVersion.Version4;
    }
}

