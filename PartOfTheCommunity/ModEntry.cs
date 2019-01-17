﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using PartOfTheCommunity.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Events;
using StardewValley.Locations;
using StardewValley.Menus;

namespace PartOfTheCommunity
{
    /// <summary>The mod entry point.</summary>
    public class ModEntry : Mod
    {
        /*********
        ** Properties
        *********/
        /// <summary>The shopkeeper names indexed by their location name.</summary>
        private readonly IDictionary<string, string> Shops = new Dictionary<string, string>
        {
            ["SeedShop"] = "Pierre",
            ["AnimalShop"] = "Marnie",
            ["Blacksmith"] = "Clint",
            ["FishShop"] = "Willy",
            ["ScienceHouse"] = "Robin",
            ["Saloon"] = "Gus",
            ["Mine"] = "Dwarf",
            ["SandyHouse"] = "Sandy",
            ["Sewer"] = "Krobus"
        };

        /// <summary>Metadata for NPCs tracked by the mod.</summary>
        private IDictionary<string, CharacterInfo> Characters;
        private bool HasEnteredEvent;
        private int CurrentNumberOfCompletedBundles;
        private uint CurrentNumberOfCompletedDailyQuests;
        private bool HasRecentlyCompletedQuest;
        private int DaysAfterCompletingLastDailyQuest = -1;
        private int CurrentUniqueItemsShipped;
        private bool IsReady;
        private ModConfig Config;


        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            GameEvents.UpdateTick += this.ModUpdate;
            TimeEvents.AfterDayStarted += this.StartTracking;
            SaveEvents.AfterReturnToTitle += this.Reset;
            SaveEvents.BeforeSave += this.EndOfDayUpdate;
            SaveEvents.AfterSave += this.SaveConfigFile;
        }


        /*********
        ** Private methods
        *********/
        private void SaveConfigFile(object sender, EventArgs e)
        {
            this.Helper.Data.WriteJsonFile($"{Constants.SaveFolderName}/config.json", this.Config);
        }

        private void StartTracking(object sender, EventArgs e)
        {
            // refresh data
            this.Characters = this.GetCharacters();
            this.CurrentNumberOfCompletedBundles = ((CommunityCenter)Game1.getLocationFromName("CommunityCenter")).numberOfCompleteBundles();
            this.CurrentUniqueItemsShipped = Game1.player.basicShipped.Count;
            this.CurrentNumberOfCompletedDailyQuests = Game1.stats.questsCompleted;
            this.HasEnteredEvent = false;

            // init on first load
            if (!this.IsReady)
            {
                // init data
                this.Config = this.Helper.Data.ReadJsonFile<ModConfig>($"{Constants.SaveFolderName}/config.json") ?? new ModConfig();
                this.IsReady = true;

                // add initial community center bonus
                if (!this.Config.HasGottenInitialUjimaBonus)
                {
                    int bonusPoints = this.Config.UjimaBonus * this.CurrentNumberOfCompletedBundles;
                    foreach (CharacterInfo shopkeeper in this.Characters.Values.Where(p => p.IsShopOwner))
                    {
                        if (shopkeeper.TryGetNpc(out NPC npc))
                            Game1.player.changeFriendship(bonusPoints, npc);
                    }
                    this.Monitor.Log($"Gained {bonusPoints} friendship from all store owners for completing {this.CurrentNumberOfCompletedBundles} {(this.CurrentNumberOfCompletedBundles > 1 ? "Bundles" : "Bundle")}", LogLevel.Info);
                    this.Config.HasGottenInitialUjimaBonus = true;
                }

                // add initial items shipped bonus
                if (!this.Config.HasGottenInitialKuumbaBonus)
                {
                    int bonusPoints = this.Config.KuumbaBonus * this.CurrentUniqueItemsShipped;
                    Utility.improveFriendshipWithEveryoneInRegion(Game1.player, bonusPoints, 2);
                    this.Monitor.Log($"Gained {bonusPoints} friendship for shipping {this.CurrentUniqueItemsShipped} unique {(this.CurrentUniqueItemsShipped != 1 ? "items" : "item")}", LogLevel.Info);
                    this.Config.HasGottenInitialKuumbaBonus = true;
                }
            }
        }

        private void Reset(object sender, EventArgs e)
        {
            this.IsReady = false;
            this.Config = null;
            this.Characters = this.GetCharacters();
            this.HasEnteredEvent = false;
            this.HasRecentlyCompletedQuest = false;
            this.CurrentNumberOfCompletedBundles = 0;
            this.CurrentNumberOfCompletedDailyQuests = 0;
            this.CurrentUniqueItemsShipped = 0;
            this.DaysAfterCompletingLastDailyQuest = 0;
        }

        private static List<Character> AreThereCharactersWithinDistance(Vector2 tile, int tilesAway, GameLocation location)
        {
            List<Character> charactersWithinDistance = new List<Character>();
            foreach (NPC character in location.characters)
            {
                if (character != null && Vector2.Distance(character.getTileLocation(), tile) <= tilesAway)
                    charactersWithinDistance.Add(character);
            }
            return charactersWithinDistance;
        }

        private void ModUpdate(object sender, EventArgs e)
        {
            if (!Context.IsWorldReady || !this.IsReady)
                return;

            foreach (KeyValuePair<string, Friendship> pair in Game1.player.friendshipData.Pairs)
            {
                // get friend info
                if (!this.Characters.TryGetValue(pair.Key, out CharacterInfo friend))
                    continue;

                // get friendship
                Friendship friendship = pair.Value;
                if (friendship.IsDivorced())
                    continue;

                // track gift
                if (friendship.GiftsToday == 1)
                    friend.ReceivedGift = true;

                // track talk & apply nearby NPC bonuses
                if (Game1.player.hasTalkedToFriendToday(friend.Name))
                {
                    if (!friend.HasTalked)
                    {
                        IList<Character> charactersWithinDistance = ModEntry.AreThereCharactersWithinDistance(Game1.player.getTileLocation(), 20, Game1.player.currentLocation);
                        foreach (Character nearbyNpc in charactersWithinDistance)
                        {
                            // get nearby character's info
                            if (nearbyNpc == null || nearbyNpc.Name == friend.Name)
                                continue;
                            if (!this.Characters.TryGetValue(nearbyNpc.Name, out CharacterInfo nearbyCharacter))
                                continue;
                            if (!Game1.player.friendshipData.TryGetValue(nearbyNpc.Name, out Friendship nearbyFriendship))
                                continue;

                            // ignore if divorced
                            if (nearbyFriendship.IsDivorced())
                            {
                                nearbyNpc.doEmote(Character.angryEmote);
                                continue;
                            }

                            // add witness bonus
                            nearbyCharacter.NearbyTalksSeen++;
                            nearbyNpc.doEmote(32);
                            Game1.player.changeFriendship(this.Config.WitnessBonus, (nearbyNpc as NPC));
                            this.Monitor.Log($"{nearbyNpc.Name} saw you taking to {friend.Name}. +{this.Config.WitnessBonus} Friendship: {nearbyNpc.Name}", LogLevel.Info);
                        }
                        friend.HasTalked = true;
                    }
                }
            }

            // check if shopping
            //TODO: Add the Bus/Pam
            if (Game1.activeClickableMenu is ShopMenu shopMenu && this.Shops.TryGetValue(Game1.currentLocation.Name, out string shopOwnerName))
            {
                // get shopkeeper
                if (!this.Characters.TryGetValue(shopOwnerName, out CharacterInfo shopkeeper))
                    return;

                // mark shopped
                if (!shopkeeper.HasShopped && this.Helper.Reflection.GetField<Item>(shopMenu, "heldItem").GetValue() != null)
                {
                    shopkeeper.HasShopped = true;
                    if (shopkeeper.TryGetNpc(out NPC shopkeeperNpc))
                    {
                        Game1.player.changeFriendship(this.Config.UjamaaBonus, shopkeeperNpc);
                        this.Monitor.Log($"{shopOwnerName}: Pleasure doing business with you!", LogLevel.Info);
                    }
                }
            }

            // check if player entered a festival
            if (!this.HasEnteredEvent && Game1.currentLocation?.currentEvent?.isFestival == true)
            {
                Utility.improveFriendshipWithEveryoneInRegion(Game1.player, this.Config.UmojaBonusFestival, 2);
                foreach (KeyValuePair<string, Friendship> pair in Game1.player.friendshipData.Pairs)
                {
                    string name = pair.Key;
                    Friendship friendship = pair.Value;
                    if (this.Characters.TryGetValue(name, out CharacterInfo character) && character.TryGetNpc(out NPC npc) && object.ReferenceEquals(npc.currentLocation, Game1.currentLocation))
                        npc.doEmote(friendship.IsDivorced() ? Character.angryEmote : Character.happyEmote);
                }
                this.Monitor.Log("The villagers are glad you came!", LogLevel.Info);
                this.HasEnteredEvent = true;
            }

            // check if player is getting married or having a baby
            if ((Game1.weddingToday || Game1.farmEvent is BirthingEvent) && !this.HasEnteredEvent && this.Characters.TryGetValue(Game1.player.spouse, out CharacterInfo spouse))
            {
                foreach (CharacterRelationship relation in spouse.Relationships)
                {
                    if (!relation.Character.TryGetNpc(out NPC relationNpc))
                        continue;

                    if (relation.IsFamily)
                    {
                        Game1.player.changeFriendship(this.Config.UmojaBonusMarry, relationNpc);
                        this.Monitor.Log($"{relation}: Married into the family, received +{this.Config.UmojaBonusMarry} friendship", LogLevel.Info);
                    }
                    else
                    {
                        Game1.player.changeFriendship(this.Config.UmojaBonusMarry / 2, relationNpc);
                        this.Monitor.Log($"{relation}: Married a friend, received +{this.Config.UmojaBonusMarry / 2} friendship", LogLevel.Info);
                    }
                }
                this.HasEnteredEvent = true;
            }

            // check if player completed daily quest
            if (Game1.stats.questsCompleted > this.CurrentNumberOfCompletedDailyQuests)
            {
                this.CurrentNumberOfCompletedDailyQuests = Game1.stats.questsCompleted;
                this.DaysAfterCompletingLastDailyQuest = 0;
                this.HasRecentlyCompletedQuest = true;
            }
        }

        private void EndOfDayUpdate(object sender, EventArgs e)
        {
            // bonus for giving gifts to an NPC's friend/relative
            foreach (CharacterInfo character in this.Characters.Values)
            {
                if (character.TryGetNpc(out NPC npc))
                    continue;

                // gifted relations bonus
                int relationsGifted = character.Relationships.Count(p => p.Character.ReceivedGift);
                if (relationsGifted > 0)
                {
                    Game1.player.changeFriendship(this.Config.StorytellerBonus * relationsGifted, npc);
                    this.Monitor.Log($"{character.Name}: Friendship raised {this.Config.StorytellerBonus * relationsGifted} for gifting to someone they love.", LogLevel.Info);
                }
            }

            // extended family bonus for gifting spouse/child
            if (!string.IsNullOrWhiteSpace(Game1.player.spouse) && this.Characters.TryGetValue(Game1.player.Name, out CharacterInfo player) && this.Characters.TryGetValue(Game1.player.spouse, out CharacterInfo spouse))
            {
                bool giftedFamily = player.Relationships.Any(p => p.Character.ReceivedGift && p.IsFamily);
                if (giftedFamily)
                {
                    foreach (CharacterRelationship relation in spouse.Relationships)
                    {
                        if (relation.Character.TryGetNpc(out NPC relationNpc) && relation.IsFamily)
                        {
                            Game1.player.changeFriendship(this.Config.UmojaBonus, relationNpc);
                            this.Monitor.Log($"{relation}: Friendship raised {this.Config.UmojaBonus} for loving your family.", LogLevel.Info);
                        }
                    }
                }
            }

            // bonus for new completed bundles
            CommunityCenter communityCenter = (CommunityCenter)Game1.getLocationFromName("CommunityCenter");
            int totalBundles = communityCenter.numberOfCompleteBundles();
            if (this.CurrentNumberOfCompletedBundles < totalBundles)
            {
                int newBundles = totalBundles - this.CurrentNumberOfCompletedBundles;
                int bonusPoints = this.Config.UjimaBonus * newBundles;
                foreach (CharacterInfo shopkeeper in this.Characters.Values.Where(p => p.IsShopOwner))
                {
                    if (shopkeeper.TryGetNpc(out NPC shopkeeperNpc))
                        Game1.player.changeFriendship(bonusPoints, shopkeeperNpc);
                }
                this.Monitor.Log($"Gained {bonusPoints} friendship with all store owners for completing {newBundles} bundles today.", LogLevel.Info);
            }

            // bonus for completed daily quests
            if (this.DaysAfterCompletingLastDailyQuest < 3 && this.HasRecentlyCompletedQuest)
            {
                int bonusPoints = this.Config.UjimaBonus / (int)Math.Pow(2, this.DaysAfterCompletingLastDailyQuest);
                Utility.improveFriendshipWithEveryoneInRegion(Game1.player, bonusPoints, 2);
                this.Monitor.Log($"Gained {bonusPoints} friendship with everyone for completing a daily quest.", LogLevel.Info);
            }
            else
            {
                if (this.DaysAfterCompletingLastDailyQuest >= 3)
                    this.HasRecentlyCompletedQuest = false;
            }
            if (!Utility.isFestivalDay(Game1.dayOfMonth + 1, Game1.currentSeason))
                this.DaysAfterCompletingLastDailyQuest++;

            // bonus for new shipped items
            if (Game1.player.basicShipped.Count > this.CurrentUniqueItemsShipped)
            {
                int bonusPoints = this.Config.KuumbaBonus * (Game1.player.basicShipped.Count - this.CurrentUniqueItemsShipped);
                Utility.improveFriendshipWithEveryoneInRegion(Game1.player, bonusPoints, 2);
                this.Monitor.Log($"Gained {bonusPoints} friendship with everyone for shipping new items.", LogLevel.Info);
            }
        }

        /// <summary>Get all available characters.</summary>
        private IDictionary<string, CharacterInfo> GetCharacters()
        {
            // get predefined characters
            IEnumerable<CharacterInfo> GetPredefinedCharacters()
            {
                // create NPCs
                var abigail = new CharacterInfo("Abigail", isMale: false);
                var alex = new CharacterInfo("Alex", isMale: true);
                var caroline = new CharacterInfo("Caroline", isMale: false);
                var clint = new CharacterInfo("Clint", isMale: true);
                var demetrius = new CharacterInfo("Demetrius", isMale: true);
                var dwarf = new CharacterInfo("Dwarf", isMale: true);
                var elliott = new CharacterInfo("Elliott", isMale: true);
                var emily = new CharacterInfo("Emily", isMale: false);
                var evelyn = new CharacterInfo("Evelyn", isMale: false);
                var george = new CharacterInfo("George", isMale: true);
                var gus = new CharacterInfo("Gus", isMale: true);
                var jas = new CharacterInfo("Jas", isMale: false);
                var jodi = new CharacterInfo("Jodi", isMale: false);
                var haley = new CharacterInfo("Haley", isMale: false);
                var kent = new CharacterInfo("Kent", isMale: true);
                var krobus = new CharacterInfo("Krobus", isMale: true);
                var leah = new CharacterInfo("Leah", isMale: false);
                var maru = new CharacterInfo("Maru", isMale: false);
                var marnie = new CharacterInfo("Marnie", isMale: false);
                var lewis = new CharacterInfo("Lewis", isMale: true);
                var pam = new CharacterInfo("Pam", isMale: false);
                var penny = new CharacterInfo("Penny", isMale: false);
                var pierre = new CharacterInfo("Pierre", isMale: true);
                var robin = new CharacterInfo("Robin", isMale: false);
                var sam = new CharacterInfo("Sam", isMale: true);
                var sandy = new CharacterInfo("Sandy", isMale: false);
                var sebastian = new CharacterInfo("Sebastian", isMale: true);
                var shane = new CharacterInfo("Shane", isMale: true);
                var vincent = new CharacterInfo("Vincent", isMale: true);
                var willy = new CharacterInfo("Willy", isMale: true);

                // Caroline's family
                this.AddRelationship(caroline, Relationship.Mother, abigail, Relationship.Daughter);
                this.AddRelationship(caroline, Relationship.Wife, pierre, Relationship.Husband);
                this.AddRelationship(pierre, Relationship.Father, abigail, Relationship.Daughter);

                // Emily's family
                this.AddRelationship(haley, Relationship.Sister, emily, Relationship.Sister);

                // Evelyn's family
                this.AddRelationship(evelyn, Relationship.Grandmother, alex, Relationship.Grandson);
                this.AddRelationship(evelyn, Relationship.Wife, george, Relationship.Husband);
                this.AddRelationship(george, Relationship.Grandfather, alex, Relationship.Grandson);

                // Jodi's family
                this.AddRelationship(jodi, Relationship.Mother, sam, Relationship.Son);
                this.AddRelationship(jodi, Relationship.Mother, vincent, Relationship.Son);
                this.AddRelationship(jodi, Relationship.Wife, kent, Relationship.Husband);
                this.AddRelationship(kent, Relationship.Father, sam, Relationship.Son);
                this.AddRelationship(kent, Relationship.Father, vincent, Relationship.Son);
                this.AddRelationship(sam, Relationship.Brother, vincent, Relationship.Brother);

                // Marnie's family
                this.AddRelationship(marnie, Relationship.Aunt, jas, Relationship.Niece);
                this.AddRelationship(marnie, Relationship.Aunt, shane, Relationship.Nephew);
                this.AddRelationship(jas, Relationship.Goddaughter, shane, Relationship.Godfather);

                // Pam's family
                this.AddRelationship(pam, Relationship.Mother, penny, Relationship.Daughter);

                // Robin's family
                this.AddRelationship(robin, Relationship.Mother, maru, Relationship.Daughter);
                this.AddRelationship(robin, Relationship.Mother, sebastian, Relationship.Son);
                this.AddRelationship(robin, Relationship.Wife, demetrius, Relationship.Husband);
                this.AddRelationship(demetrius, Relationship.Father, maru, Relationship.Daughter);
                this.AddRelationship(demetrius, Relationship.StepFather, sebastian, Relationship.StepSon);
                this.AddRelationship(maru, Relationship.HalfSister, sebastian, Relationship.HalfBrother);

                // friends
                this.AddFriend(abigail, sam);
                this.AddFriend(abigail, sebastian);
                this.AddFriend(caroline, kent);
                this.AddFriend(elliott, willy);
                this.AddFriend(emily, clint);
                this.AddFriend(emily, gus);
                this.AddFriend(emily, sandy);
                this.AddFriend(emily, shane);
                this.AddFriend(haley, alex);
                this.AddFriend(jas, vincent);
                this.AddFriend(jodi, caroline);
                this.AddFriend(leah, elliott);
                this.AddFriend(marnie, lewis);
                this.AddFriend(pam, gus);
                this.AddFriend(penny, maru);
                this.AddFriend(penny, sam);
                this.AddFriend(sam, sebastian);

                // other
                this.AddRelationship(dwarf, Relationship.WarTorn, krobus, Relationship.WarTorn);

                return new[]
                {
                    abigail,
                    alex,
                    caroline,
                    clint,
                    demetrius,
                    dwarf,
                    elliott,
                    emily,
                    evelyn,
                    george,
                    gus,
                    jas,
                    jodi,
                    haley,
                    kent,
                    krobus,
                    leah,
                    maru,
                    marnie,
                    lewis,
                    pam,
                    penny,
                    pierre,
                    robin,
                    sam,
                    sandy,
                    sebastian,
                    shane,
                    vincent,
                    willy
                };
            }
            IDictionary<string, CharacterInfo> characters = GetPredefinedCharacters().ToDictionary(p => p.Name);

            // mark shopkeepers
            {
                HashSet<string> shopkeeperNames = new HashSet<string>(this.Shops.Values);
                foreach (CharacterInfo character in characters.Values)
                    character.IsShopOwner = shopkeeperNames.Contains(character.Name);
            }

            // add player
            var player = new CharacterInfo(Game1.player.Name, isMale: Game1.player.IsMale, type: CharacterType.Player);
            characters[player.Name] = player;

            // add player spouse
            CharacterInfo spouse = null;
            if (Game1.player.spouse != null)
            {
                if (!characters.TryGetValue(Game1.player.spouse, out spouse))
                {
                    spouse = new CharacterInfo(Game1.player.spouse, isMale: Utility.isMale(Game1.player.spouse));
                    characters[spouse.Name] = spouse;
                }
            }

            // add unknown NPCs
            List<NPC> allCharacters = new List<NPC>();
            Utility.getAllCharacters(allCharacters);
            foreach (NPC npc in allCharacters)
            {
                if (npc.isVillager() && !characters.ContainsKey(npc.Name))
                    characters[npc.Name] = new CharacterInfo(npc.Name, npc.Gender == NPC.male, type: CharacterType.Child);
            }

            // add children
            foreach (Child childNpc in Game1.player.getChildren())
            {
                // add child
                CharacterInfo child = new CharacterInfo(childNpc.Name, isMale: childNpc.Gender == NPC.male);
                characters[child.Name] = child;

                // add relationships
                this.AddRelationship(player, player.IsMale ? Relationship.Father : Relationship.Mother, child, child.IsMale ? Relationship.Son : Relationship.Daughter);
                if (spouse != null)
                {
                    foreach (CharacterRelationship parentRelation in spouse.Relationships)
                    {
                        switch (parentRelation.Relationship)
                        {
                            case Relationship.Grandfather:
                            case Relationship.Grandmother:
                                this.AddRelationship(child, child.IsMale ? Relationship.GreatGrandson : Relationship.GreatGranddaughter, parentRelation.Character, parentRelation.Character.IsMale ? Relationship.GreatGrandfather : Relationship.GreatGrandmother);
                                break;

                            case Relationship.Father:
                            case Relationship.Mother:
                            case Relationship.StepFather:
                            case Relationship.StepMother:
                                this.AddRelationship(child, child.IsMale ? Relationship.Grandson : Relationship.Granddaughter, parentRelation.Character, parentRelation.Character.IsMale ? Relationship.Grandfather : Relationship.Grandmother);
                                break;

                            case Relationship.Brother:
                            case Relationship.Sister:
                            case Relationship.HalfBrother:
                            case Relationship.HalfSister:
                                this.AddRelationship(child, child.IsMale ? Relationship.Nephew : Relationship.Niece, parentRelation.Character, parentRelation.Character.IsMale ? Relationship.Uncle : Relationship.Aunt);
                                break;

                            case Relationship.Niece:
                            case Relationship.Nephew:
                                this.AddRelationship(child, Relationship.Cousin, parentRelation.Character, Relationship.Cousin);
                                break;

                            case Relationship.Son:
                            case Relationship.Daughter:
                                this.AddRelationship(child, child.IsMale ? Relationship.Brother : Relationship.Sister, parentRelation.Character, parentRelation.Character.IsMale ? Relationship.Brother : Relationship.Sister);
                                break;
                        }
                    }
                    this.AddRelationship(spouse, spouse.IsMale ? Relationship.Father : Relationship.Mother, child, child.IsMale ? Relationship.Son : Relationship.Daughter);
                }
            }

            return characters;
        }

        /// <summary>Add a relationship between two NPCs.</summary>
        /// <param name="left">The left NPC.</param>
        /// <param name="leftType">The left relationship type (i.e. <paramref name="left"/> is the _____ of <paramref name="right"/>).</param>
        /// <param name="right">The right NPC.</param>
        /// <param name="rightType">The right relationship type (i.e. <paramref name="right"/> is the _____ of <paramref name="left"/>).</param>
        private void AddRelationship(CharacterInfo left, Relationship leftType, CharacterInfo right, Relationship rightType)
        {
            left.AddRelationship(rightType, right);
            right.AddRelationship(leftType, left);
        }

        /// <summary>Add a friend relationship between two NPCs.</summary>
        /// <param name="left">The left NPC.</param>
        /// <param name="right">The right NPC.</param>
        private void AddFriend(CharacterInfo left, CharacterInfo right)
        {
            this.AddRelationship(left, Relationship.Friend, right, Relationship.Friend);
        }
    }
}
