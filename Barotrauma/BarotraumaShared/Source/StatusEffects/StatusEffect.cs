﻿using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class DurationListElement
    {
        public StatusEffect Parent;
        public Entity Entity;
        public List<ISerializableEntity> Targets;
        public float Timer;
    }
    
    partial class StatusEffect
    {
        [Flags]
        public enum TargetType
        {
            This = 1,
            Parent = 2,
            Character = 4,
            Contained = 8,
            NearbyCharacters = 16,
            NearbyItems = 32,
            UseTarget = 64,
            Hull = 128,
            Limb = 256,
            AllLimbs = 512
        }

        class ItemSpawnInfo
        {
            public enum SpawnPositionType
            {
                This,
                ThisInventory,
                ContainedInventory
            }

            public readonly ItemPrefab ItemPrefab;
            public readonly SpawnPositionType SpawnPosition;
            public readonly float Speed;
            public readonly float Rotation;

            public ItemSpawnInfo(XElement element, string parentDebugName)
            {
                if (element.Attribute("name") != null)
                {
                    //backwards compatibility
                    DebugConsole.ThrowError("Error in StatusEffect config (" + element.ToString() + ") - use item identifier instead of the name.");
                    string itemPrefabName = element.GetAttributeString("name", "");
                    ItemPrefab = MapEntityPrefab.List.Find(m => m is ItemPrefab && (m.NameMatches(itemPrefabName) || m.Tags.Contains(itemPrefabName))) as ItemPrefab;
                    if (ItemPrefab == null)
                    {
                        DebugConsole.ThrowError("Error in StatusEffect \""+ parentDebugName + "\" - item prefab \"" + itemPrefabName + "\" not found.");
                    }
                }
                else
                {
                    string itemPrefabIdentifier = element.GetAttributeString("identifier", "");
                    if (string.IsNullOrEmpty(itemPrefabIdentifier)) itemPrefabIdentifier = element.GetAttributeString("identifiers", "");
                    if (string.IsNullOrEmpty(itemPrefabIdentifier))
                    {
                        DebugConsole.ThrowError("Invalid item spawn in StatusEffect \"" + parentDebugName + "\" - identifier not found in the element \"" + element.ToString() + "\"");
                    }
                    ItemPrefab = MapEntityPrefab.List.Find(m => m is ItemPrefab && m.Identifier == itemPrefabIdentifier) as ItemPrefab;
                    if (ItemPrefab == null)
                    {
                        DebugConsole.ThrowError("Error in StatusEffect config - item prefab with the identifier \"" + itemPrefabIdentifier + "\" not found.");
                        return;
                    }
                }
                
                Speed = element.GetAttributeFloat("speed", 0.0f);
                Rotation = MathHelper.ToRadians(element.GetAttributeFloat("rotation", 0.0f));

                string spawnTypeStr = element.GetAttributeString("spawnposition", "This");
                if (!Enum.TryParse(spawnTypeStr, out SpawnPosition))
                {
                    DebugConsole.ThrowError("Error in StatusEffect config - \"" + spawnTypeStr + "\" is not a valid spawn position.");
                }
            }
        }


        private TargetType targetTypes;
        protected HashSet<string> targetIdentifiers;

        private List<RelatedItem> requiredItems;
        
        public string[] propertyNames;
        private object[] propertyEffects;

        private PropertyConditional.Comparison conditionalComparison = PropertyConditional.Comparison.Or;
        private List<PropertyConditional> propertyConditionals;

        private bool setValue;
        
        private bool disableDeltaTime;
        
        private HashSet<string> tags;
        
        private readonly float duration;
        public static readonly List<DurationListElement> DurationList = new List<DurationListElement>();

        public bool CheckConditionalAlways; //Always do the conditional checks for the duration/delay. If false, only check conditional on apply.

        public bool Stackable = true; //Can the same status effect be applied several times to the same targets?

        private readonly int useItemCount;
        
        private readonly bool removeItem;

        public readonly ActionType type = ActionType.OnActive;

        private Explosion explosion;

        private List<ItemSpawnInfo> spawnItems;

        public readonly float FireSize;
        
        public HashSet<string> TargetIdentifiers
        {
            get { return targetIdentifiers; }
        }
        
        public List<Affliction> Afflictions
        {
            get;
            private set;
        }

        private List<Pair<string, float>> ReduceAffliction;

        //only applicable if targeting NearbyCharacters or NearbyItems
        public float Range
        {
            get;
            private set;
        }

        public string Tags
        {
            get { return string.Join(",", tags); }
            set
            {
                tags.Clear();
                if (value == null) return;

                string[] newTags = value.Split(',');
                foreach (string tag in newTags)
                {
                    string newTag = tag.Trim();
                    if (!tags.Contains(newTag)) tags.Add(newTag);
                }

            }
        }

        public static StatusEffect Load(XElement element, string parentDebugName)
        {
            if (element.Attribute("delay") != null)
            {
                return new DelayedEffect(element, parentDebugName);
            }

            return new StatusEffect(element, parentDebugName);
        }

        protected StatusEffect(XElement element, string parentDebugName)
        {
            requiredItems = new List<RelatedItem>();
            spawnItems = new List<ItemSpawnInfo>();
            Afflictions = new List<Affliction>();
            ReduceAffliction = new List<Pair<string, float>>();
            tags = new HashSet<string>(element.GetAttributeString("tags", "").Split(','));

            Range = element.GetAttributeFloat("range", 0.0f);

            IEnumerable<XAttribute> attributes = element.Attributes();
            List<XAttribute> propertyAttributes = new List<XAttribute>();
            propertyConditionals = new List<PropertyConditional>();

            foreach (XAttribute attribute in attributes)
            {
                switch (attribute.Name.ToString())
                {
                    case "type":
                        if (!Enum.TryParse(attribute.Value, true, out type))
                        {
                            DebugConsole.ThrowError("Invalid action type \"" + attribute.Value + "\" in StatusEffect (" + parentDebugName + ")");
                        }
                        break;
                    case "target":
                        string[] Flags = attribute.Value.Split(',');
                        foreach (string s in Flags)
                        {
                            if (!Enum.TryParse(s, true, out TargetType targetType))
                            {
                                DebugConsole.ThrowError("Invalid target type \"" + s + "\" in StatusEffect (" + parentDebugName + ")");
                            }
                            else
                            {
                                targetTypes |= targetType;
                            }
                        }
                        break;
                    case "disabledeltatime":
                        disableDeltaTime = attribute.GetAttributeBool(false);
                        break;
                    case "setvalue":
                        setValue = attribute.GetAttributeBool(false);
                        break;
                    case "targetnames":
                        DebugConsole.ThrowError("Error in StatusEffect config (" + parentDebugName + ") - use identifiers or tags to define the targets instead of names.");
                        break;
                    case "targetidentifiers":
                        string[] identifiers = attribute.Value.Split(',');
                        targetIdentifiers = new HashSet<string>();
                        for (int i = 0; i < identifiers.Length; i++)
                        {
                            targetIdentifiers.Add(identifiers[i].Trim().ToLowerInvariant());
                        }
                        break;
                    case "duration":
                        duration = attribute.GetAttributeFloat(0.0f);
                        break;
                    case "stackable":
                        Stackable = attribute.GetAttributeBool(true);
                        break;
                    case "checkconditionalalways":
                        CheckConditionalAlways = attribute.GetAttributeBool(false);
                        break;
                    case "conditionalcomparison":
                    case "comparison":
                        if (!Enum.TryParse(attribute.Value, out conditionalComparison))
                        {
                            DebugConsole.ThrowError("Invalid conditional comparison type \"" + attribute.Value + "\" in StatusEffect (" + parentDebugName + ")");
                        }
                        break;
                    case "sound":
                        DebugConsole.ThrowError("Error in StatusEffect " + element.Parent.Name.ToString() +
                            " - sounds should be defined as child elements of the StatusEffect, not as attributes.");
                        break;
                    default:
                        propertyAttributes.Add(attribute);
                        break;
                }
            }

            int count = propertyAttributes.Count;
            propertyNames = new string[count];
            propertyEffects = new object[count];

            int n = 0;
            foreach (XAttribute attribute in propertyAttributes)
            {
                propertyNames[n] = attribute.Name.ToString().ToLowerInvariant();
                propertyEffects[n] = XMLExtensions.GetAttributeObject(attribute);
                n++;
            }

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "explosion":
                        explosion = new Explosion(subElement, parentDebugName);
                        break;
                    case "fire":
                        FireSize = subElement.GetAttributeFloat("size",10.0f);
                        break;
                    case "use":
                    case "useitem":
                        useItemCount++;
                        break;
                    case "remove":
                    case "removeitem":
                        removeItem = true;
                        break;
                    case "requireditem":
                    case "requireditems":
                        RelatedItem newRequiredItem = RelatedItem.Load(subElement, parentDebugName);
                        if (newRequiredItem == null)
                        {
                            DebugConsole.ThrowError("Error in StatusEffect config - requires an item with no identifiers.");
                            continue;
                        }
                        requiredItems.Add(newRequiredItem);
                        break;
                    case "conditional":
                        IEnumerable<XAttribute> conditionalAttributes = subElement.Attributes();
                        foreach (XAttribute attribute in conditionalAttributes)
                        {
                            if (attribute.Name.ToString().ToLowerInvariant() == "targetitemcomponent") { continue; }
                            propertyConditionals.Add(new PropertyConditional(attribute));
                        }
                        break;
                    case "affliction":
                        AfflictionPrefab afflictionPrefab;
                        if (subElement.Attribute("name") != null)
                        {
                            DebugConsole.ThrowError("Error in StatusEffect (" + parentDebugName + ") - define afflictions using identifiers instead of names.");
                            string afflictionName = subElement.GetAttributeString("name", "").ToLowerInvariant();
                            afflictionPrefab = AfflictionPrefab.List.Find(ap => ap.Name.ToLowerInvariant() == afflictionName);
                            if (afflictionPrefab == null)
                            {
                                DebugConsole.ThrowError("Error in StatusEffect (" + parentDebugName + ") - Affliction prefab \"" + afflictionName + "\" not found.");
                                continue;
                            }
                        }
                        else
                        {
                            string afflictionIdentifier = subElement.GetAttributeString("identifier", "").ToLowerInvariant();
                            afflictionPrefab = AfflictionPrefab.List.Find(ap => ap.Identifier.ToLowerInvariant() == afflictionIdentifier);
                            if (afflictionPrefab == null)
                            {
                                DebugConsole.ThrowError("Error in StatusEffect (" + parentDebugName + ") - Affliction prefab with the identifier \"" + afflictionIdentifier + "\" not found.");
                                continue;
                            }
                        }

                        float afflictionStrength = subElement.GetAttributeFloat(1.0f, "amount", "strength");
                        Afflictions.Add(afflictionPrefab.Instantiate(afflictionStrength));
                        
                        break;
                    case "reduceaffliction":
                        if (subElement.Attribute("name") != null)
                        {
                            DebugConsole.ThrowError("Error in StatusEffect (" + parentDebugName + ") - define afflictions using identifiers or types instead of names.");
                            ReduceAffliction.Add(new Pair<string, float>(
                                subElement.GetAttributeString("name", "").ToLowerInvariant(),
                                subElement.GetAttributeFloat(1.0f, "amount", "strength", "reduceamount")));
                        }
                        else
                        {
                            string name = subElement.GetAttributeString("identifier", null) ?? subElement.GetAttributeString("type", null);
                            name = name.ToLowerInvariant();

                            if (AfflictionPrefab.List.Any(ap => ap.Identifier == name || ap.AfflictionType == name))
                            {
                                ReduceAffliction.Add(new Pair<string, float>(
                                    name,
                                    subElement.GetAttributeFloat(1.0f, "amount", "strength", "reduceamount")));
                            }
                            else
                            {
                                DebugConsole.ThrowError("Error in StatusEffect (" + parentDebugName + ") - Affliction prefab with the identifier or type \"" + name + "\" not found.");
                            }
                        }
                        break;
                    case "spawnitem":
                        var newSpawnItem = new ItemSpawnInfo(subElement, parentDebugName);
                        if (newSpawnItem.ItemPrefab != null) spawnItems.Add(newSpawnItem);
                        break;
                }
            }
            InitProjSpecific(element, parentDebugName);
        }

        partial void InitProjSpecific(XElement element, string parentDebugName);

        public bool HasTargetType(TargetType targetType)
        {
            return (targetTypes & targetType) != 0;
        }

        public virtual bool HasRequiredItems(Entity entity)
        {
            if (requiredItems == null) return true;
            foreach (RelatedItem requiredItem in requiredItems)
            {
                if (entity == null)
                {
                    return false;
                }
                else if (entity is Item item)
                {
                    if (!requiredItem.CheckRequirements(null, item)) return false;
                }
                else if (entity is Character character)
                {
                    if (!requiredItem.CheckRequirements(character, null)) return false;
                }
            }
            return true;
        }

        public void GetNearbyTargets(Vector2 worldPosition, List<ISerializableEntity> targets)
        {
            if (Range <= 0.0f) { return; }
            if (HasTargetType(TargetType.NearbyCharacters))
            {
                foreach (Character c in Character.CharacterList)
                {
                    if (!c.Enabled || c.Removed || !IsValidTarget(c)) { continue; }
                    float xDiff = Math.Abs(c.WorldPosition.X - worldPosition.X);
                    if (xDiff > Range) { continue; }
                    float yDiff = Math.Abs(c.WorldPosition.Y - worldPosition.Y);
                    if (yDiff > Range) { continue; }

                    if (xDiff * xDiff + yDiff * yDiff < Range * Range) { targets.Add(c); }
                }
            }
            if (HasTargetType(TargetType.NearbyItems))
            {
                foreach (Item item in Item.ItemList)
                {
                    if (item.Removed || !IsValidTarget(item)) { continue; }
                    float xDiff = Math.Abs(item.WorldPosition.X - worldPosition.X);
                    if (xDiff > Range) { continue; }
                    float yDiff = Math.Abs(item.WorldPosition.Y - worldPosition.Y);
                    if (yDiff > Range) { continue; }

                    if (xDiff * xDiff + yDiff * yDiff < Range * Range) { targets.Add(item); }
                }
            }
        }

        public virtual bool HasRequiredConditions(List<ISerializableEntity> targets)
        {
            if (!propertyConditionals.Any()) return true;
            switch (conditionalComparison)
            {
                case PropertyConditional.Comparison.Or:
                    foreach (ISerializableEntity target in targets)
                    {
                        if (target == null || target.SerializableProperties == null) { continue; }
                        foreach (PropertyConditional pc in propertyConditionals)
                        {
                            if (!string.IsNullOrEmpty(pc.TargetItemComponentName))
                            {
                                if (!(target is ItemComponent ic) || ic.Name != pc.TargetItemComponentName)
                                {
                                    continue;
                                }
                            }
                            if (pc.Matches(target)) { return true; }
                        }
                    }
                    return false;
                case PropertyConditional.Comparison.And:
                    foreach (ISerializableEntity target in targets)
                    {
                        if (target == null || target.SerializableProperties == null) { continue; }
                        foreach (PropertyConditional pc in propertyConditionals)
                        {
                            if (!string.IsNullOrEmpty(pc.TargetItemComponentName))
                            {
                                if (!(target is ItemComponent ic) || ic.Name != pc.TargetItemComponentName)
                                {
                                    continue;
                                }
                            }
                            if (!pc.Matches(target)) { return false; }
                        }
                    }
                    return true;
                default:
                    throw new NotImplementedException();
            }
        }

        protected bool IsValidTarget(ISerializableEntity entity)
        {
            if (targetIdentifiers == null) { return true; }

            if (entity is Item item)
            {
                if (item.HasTag(targetIdentifiers)) return true;
                if (targetIdentifiers.Any(id => id == item.Prefab.Identifier)) return true;
            }
            else if (entity is ItemComponent itemComponent)
            {
                if (itemComponent.Item.HasTag(targetIdentifiers)) return true;
                if (targetIdentifiers.Any(id => id == itemComponent.Item.Prefab.Identifier)) return true;
            }
            else if (entity is Structure structure)
            {
                if (targetIdentifiers.Any(id => id == structure.Prefab.Identifier)) return true;
            }
            else if (entity is Character character)
            {
                if (targetIdentifiers.Any(id => id == character.SpeciesName)) return true;
            }

            return targetIdentifiers.Any(id => id == entity.Name);
        }

        public void SetUser(Character user)
        {
            foreach (Affliction affliction in Afflictions)
            {
                affliction.Source = user;
            }
        }

        public virtual void Apply(ActionType type, float deltaTime, Entity entity, ISerializableEntity target)
        {
            if (this.type != type || !HasRequiredItems(entity)) return;

            if (targetIdentifiers != null && !IsValidTarget(target)) return;
            
            if (duration > 0.0f && !Stackable)
            {
                //ignore if not stackable and there's already an identical statuseffect
                DurationListElement existingEffect = DurationList.Find(d => d.Parent == this && d.Targets.FirstOrDefault() == target);
                if (existingEffect != null)
                {
                    existingEffect.Timer = Math.Max(existingEffect.Timer, duration);
                    return;
                }
            }

            List<ISerializableEntity> targets = new List<ISerializableEntity> { target };

            if (!HasRequiredConditions(targets)) return;

            Apply(deltaTime, entity, targets);
        }

        protected readonly List<ISerializableEntity> currentTargets = new List<ISerializableEntity>();
        public virtual void Apply(ActionType type, float deltaTime, Entity entity, IEnumerable<ISerializableEntity> targets)
        {
            if (this.type != type) return;

            currentTargets.Clear();
            foreach (ISerializableEntity target in targets)
            {
                if (targetIdentifiers != null)
                {
                    //ignore invalid targets
                    if (!IsValidTarget(target)) { continue; }
                }
                currentTargets.Add(target);
            }

            if (targetIdentifiers != null && currentTargets.Count == 0) { return; }

            if (!HasRequiredItems(entity) || !HasRequiredConditions(currentTargets)) return;

            if (duration > 0.0f && !Stackable)
            {
                //ignore if not stackable and there's already an identical statuseffect
                DurationListElement existingEffect = DurationList.Find(d => d.Parent == this && d.Targets.SequenceEqual(currentTargets));
                if (existingEffect != null)
                {
                    existingEffect.Timer = Math.Max(existingEffect.Timer, duration);
                    return;
                }
            }

            Apply(deltaTime, entity, currentTargets);
        }

        protected void Apply(float deltaTime, Entity entity, List<ISerializableEntity> targets)
        {
            Hull hull = null;
            if (entity is Character)
            {
                hull = ((Character)entity).AnimController.CurrentHull;
            }
            else if (entity is Item)
            {
                hull = ((Item)entity).CurrentHull;
            }

            foreach (ISerializableEntity serializableEntity in targets)
            {
                if (!(serializableEntity is Item item)) continue;

                Character targetCharacter = targets.FirstOrDefault(t => t is Character character && !character.Removed) as Character;
                if (targetCharacter == null)
                {
                    foreach (var target in targets)
                    {
                        if (target is Limb limb && limb.character != null && !limb.character.Removed) targetCharacter = ((Limb)target).character;
                    }
                }
                for (int i = 0; i < useItemCount; i++)
                {
                    if (item.Removed) continue;
                    item.Use(deltaTime, targetCharacter, targets.FirstOrDefault(t => t is Limb) as Limb);
                }
            }                     

            if (removeItem)
            {
                foreach (Item item in targets.Where(t => t is Item).Cast<Item>())
                {
                    Entity.Spawner?.AddToRemoveQueue(item);
                }
            }

            if (duration > 0.0f)
            {
                DurationListElement element = new DurationListElement
                {
                    Parent = this,
                    Timer = duration,
                    Entity = entity,
                    Targets = targets
                };

                DurationList.Add(element);
            }
            else
            {
                foreach (ISerializableEntity target in targets)
                {
                    if (target is Entity targetEntity)
                    {
                        if (targetEntity.Removed) continue;
                    }

                    for (int i = 0; i < propertyNames.Length; i++)
                    {
                        if (target == null || target.SerializableProperties == null || 
                            !target.SerializableProperties.TryGetValue(propertyNames[i], out SerializableProperty property)) continue;
                        ApplyToProperty(target, property, propertyEffects[i], deltaTime);
                    }
                }                
            }

            if (explosion != null && entity != null) explosion.Explode(entity.WorldPosition, entity);

            foreach (ISerializableEntity target in targets)
            {
                foreach (Affliction affliction in Afflictions)
                {
                    Affliction multipliedAffliction = affliction;
                    if (!disableDeltaTime) multipliedAffliction = affliction.CreateMultiplied(deltaTime);

                    if (target is Character character)
                    {
                        character.LastDamageSource = entity;
                        foreach (Limb limb in character.AnimController.Limbs)
                        {
                            limb.character.DamageLimb(entity.WorldPosition, limb, new List<Affliction>() { multipliedAffliction }, stun: 0.0f, playSound: false, attackImpulse: 0.0f, attacker: affliction.Source);
                            //only apply non-limb-specific afflictions to the first limb
                            if (!affliction.Prefab.LimbSpecific) { break; }
                        }
                    }
                    else if (target is Limb limb)
                    {
                        limb.character.DamageLimb(entity.WorldPosition, limb, new List<Affliction>() { multipliedAffliction }, stun: 0.0f, playSound: false, attackImpulse: 0.0f, attacker: affliction.Source);
                    }
                }

                foreach (Pair<string, float> reduceAffliction in ReduceAffliction)
                {
                    float reduceAmount = disableDeltaTime ? reduceAffliction.Second : reduceAffliction.Second * deltaTime;
                    if (target is Character character)
                    {
                        character.CharacterHealth.ReduceAffliction(null, reduceAffliction.First, reduceAmount);
                    }
                    else if (target is Limb limb)
                    {
                        limb.character.CharacterHealth.ReduceAffliction(limb, reduceAffliction.First, reduceAmount);
                    }
                }
            }

            if (FireSize > 0.0f && entity != null)
            {
                var fire = new FireSource(entity.WorldPosition, hull);
                fire.Size = new Vector2(FireSize, fire.Size.Y);
            }
            
            bool isNotClient = GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient;
            if (isNotClient && entity != null && Entity.Spawner != null) //clients are not allowed to spawn items
            {
                foreach (ItemSpawnInfo itemSpawnInfo in spawnItems)
                {
                    switch (itemSpawnInfo.SpawnPosition)
                    {
                        case ItemSpawnInfo.SpawnPositionType.This:
                            Entity.Spawner.AddToSpawnQueue(itemSpawnInfo.ItemPrefab, entity.WorldPosition);
                            break;
                        case ItemSpawnInfo.SpawnPositionType.ThisInventory:
                            { 
                                if (entity is Character character)
                                {
                                    if (character.Inventory != null && character.Inventory.Items.Any(it => it == null))
                                    {
                                        Entity.Spawner.AddToSpawnQueue(itemSpawnInfo.ItemPrefab, character.Inventory);
                                    }
                                }
                                else if (entity is Item item)
                                {
                                    var inventory = item?.GetComponent<ItemContainer>()?.Inventory;
                                    if (inventory != null && inventory.Items.Any(it => it == null))
                                    {
                                        Entity.Spawner.AddToSpawnQueue(itemSpawnInfo.ItemPrefab, inventory);
                                    }
                                }
                            }
                            break;
                        case ItemSpawnInfo.SpawnPositionType.ContainedInventory:
                            {
                                Inventory thisInventory = null;
                                if (entity is Character character)
                                {
                                    thisInventory = character.Inventory;
                                }
                                else if (entity is Item item)
                                {
                                    thisInventory = item?.GetComponent<ItemContainer>()?.Inventory;
                                }
                                if (thisInventory != null)
                                {
                                    foreach (Item item in thisInventory.Items)
                                    {
                                        if (item == null) continue;
                                        Inventory containedInventory = item.GetComponent<ItemContainer>()?.Inventory;
                                        if (containedInventory == null || !containedInventory.Items.Any(i => i == null)) continue;
                                        Entity.Spawner.AddToSpawnQueue(itemSpawnInfo.ItemPrefab, containedInventory);
                                        break;
                                    }
                                }                                
                            }
                            break;
                    }
                }
            }

            ApplyProjSpecific(deltaTime, entity, targets, hull);
        }

        partial void ApplyProjSpecific(float deltaTime, Entity entity, List<ISerializableEntity> targets, Hull currentHull);

        private void ApplyToProperty(ISerializableEntity target, SerializableProperty property, object value, float deltaTime)
        {
            if (disableDeltaTime || setValue) deltaTime = 1.0f;

            Type type = value.GetType();
            if (type == typeof(float) ||
                (type == typeof(int) && property.GetValue(target) is float))
            {
                float floatValue = Convert.ToSingle(value) * deltaTime;

                if (!setValue) floatValue += (float)property.GetValue(target);
                property.TrySetValue(target, floatValue);
            }
            else if (type == typeof(int) && value is int)
            {
                int intValue = (int)((int)value * deltaTime);
                if (!setValue) intValue += (int)property.GetValue(target);
                property.TrySetValue(target, intValue);
            }
            else if (type == typeof(bool) && value is bool)
            {
                property.TrySetValue(target, (bool)value);
            }
            else if (type == typeof(string))
            {
                property.TrySetValue(target, (string)value);
            }
            else
            {
                DebugConsole.ThrowError("Couldn't apply value " + value.ToString() + " (" + type + ") to property \"" + property.Name + "\" (" + property.GetValue(target).GetType() + ")! "
                    + "Make sure the type of the value set in the config files matches the type of the property.");
            }
        }

        public static void UpdateAll(float deltaTime)
        {
            UpdateAllProjSpecific(deltaTime);

            DelayedEffect.Update(deltaTime);
            for (int i = DurationList.Count - 1; i >= 0; i--)
            {
                DurationListElement element = DurationList[i];

                if (element.Parent.CheckConditionalAlways && !element.Parent.HasRequiredConditions(element.Targets))
                {
                    DurationList.RemoveAt(i);
                    continue;
                }

                element.Targets.RemoveAll(t => 
                    (t is Entity entity && entity.Removed) || 
                    (t is Limb limb && (limb.character == null || limb.character.Removed)));
                if (element.Targets.Count == 0)
                {
                    DurationList.RemoveAt(i);
                    continue;
                }

                foreach (ISerializableEntity target in element.Targets)
                {
                    for (int n = 0; n < element.Parent.propertyNames.Length; n++)
                    {
                        if (target == null ||
                            target.SerializableProperties == null ||
                            !target.SerializableProperties.TryGetValue(element.Parent.propertyNames[n], out SerializableProperty property))
                        {
                            continue;
                        }
                        element.Parent.ApplyToProperty(target, property, element.Parent.propertyEffects[n], CoroutineManager.UnscaledDeltaTime);
                    }

                    foreach (Affliction affliction in element.Parent.Afflictions)
                    {
                        Affliction multipliedAffliction = affliction;
                        if (!element.Parent.disableDeltaTime) { multipliedAffliction = affliction.CreateMultiplied(deltaTime); }

                        if (target is Character character)
                        {
                            character.AddDamage(character.WorldPosition, new List<Affliction>() { multipliedAffliction }, stun: 0.0f, playSound: false);
                        }
                        else if (target is Limb limb)
                        {
                            limb.character.DamageLimb(limb.WorldPosition, limb, new List<Affliction>() { multipliedAffliction }, stun: 0.0f, playSound: false, attackImpulse: 0.0f);
                        }
                    }

                    foreach (Pair<string, float> reduceAffliction in element.Parent.ReduceAffliction)
                    {
                        if (target is Character)
                        {
                            ((Character)target).CharacterHealth.ReduceAffliction(null, reduceAffliction.First, reduceAffliction.Second * deltaTime);
                        }
                        else if (target is Limb limb)
                        {
                            limb.character.CharacterHealth.ReduceAffliction(limb, reduceAffliction.First, reduceAffliction.Second * deltaTime);
                        }
                    }
                }

                element.Timer -= deltaTime;

                if (element.Timer > 0.0f) continue;
                DurationList.Remove(element);
            }
        }

        static partial void UpdateAllProjSpecific(float deltaTime);

        public static void StopAll()
        {
            CoroutineManager.StopCoroutines("statuseffect");
            DelayedEffect.DelayList.Clear();
            DurationList.Clear();
#if CLIENT
            //ActiveLoopingSounds.Clear();
#endif
        }

        public void AddTag(string tag)
        {
            if (tags.Contains(tag)) return;
            tags.Add(tag);
        }

        public bool HasTag(string tag)
        {
            if (tag == null) return true;

            return (tags.Contains(tag) || tags.Contains(tag.ToLowerInvariant()));
        }
    }
}
