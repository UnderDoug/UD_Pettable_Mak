using HarmonyLib;

using System;
using System.Collections.Generic;

using XRL.Language;
using XRL.World;
using XRL.World.AI;
using XRL.World.Parts;
using XRL.World.Parts.Mutation;

namespace XRL.World.Parts
{
    [HarmonyPatch]
    [Serializable]
    public class UD_DoNotPetMe : IScribedPart
    {
        public static ModInfo ThisMod = ModManager.GetMod("ud_pettable_mak");

        public List<string> WarnedList = new();
        public string WarnMessage;
        public string WarnFilter;
        public string AngerMessage;
        public string ConsumeFloatMessage;

        public UD_DoNotPetMe() 
        {
            WarnMessage = "Best not be doing that gain!";
            AngerMessage = "YOU WERE WARNED";
        }

        public bool HasWarned(GameObject Petter)
        {
            return Petter != null
                && WarnedList.Contains(Petter.ID);
        }
        public bool Warn(GameObject Petter)
        {
            bool alreadyWarned = HasWarned(Petter);

            if (!alreadyWarned)
            {
                WarnedList.Add(Petter.ID);
                bool allowSecondPerson = Grammar.AllowSecondPerson;
                Grammar.AllowSecondPerson = false;
                string message = GameText.VariableReplace(WarnMessage, ParentObject, Petter);
                Grammar.AllowSecondPerson = allowSecondPerson;
                EmitMessage(TextFilters.Filter(message, WarnFilter), ' ', FromDialog: false, Petter.IsPlayerControlled() || ParentObject.IsPlayerControlled());
                MetricsManager.LogModInfo(ThisMod, $"Warned");
            }
            return !alreadyWarned && HasWarned(Petter);
        }

        public static bool SwallowWhole(GameObject Petter, GameObject Pettee, string ConsumeFloatMessage)
        {
            if (Petter != null && Pettee != null)
            {
                int weightThresholdPercentage = 999999;
                int weightThreshold = Pettee.Weight * weightThresholdPercentage / 100;
                MetricsManager.LogModInfo(ThisMod, $"{nameof(weightThresholdPercentage)}: {weightThresholdPercentage}");
                MetricsManager.LogModInfo(ThisMod, $"{nameof(weightThreshold)}: {weightThreshold}");

                Consumer consumer = Pettee.RequirePart<Consumer>();
                consumer.Active = true;
                consumer.WeightThresholdPercentage = weightThresholdPercentage;
                consumer.Message = "{{R|=subject.T= =verb:swallow= =object.Name= whole for petting =pronouns.objective= too many times!}}";
                consumer.FloatMessage = ConsumeFloatMessage;

                if (consumer.CanConsume(Petter) && !consumer.ShouldIgnore(Petter))
                {
                    CombatJuiceEntryPunch punch = CombatJuice.punch(Pettee, Petter, 0.1f);
                    CombatJuiceManager.enqueueEntry(punch, async: true);

                    Pettee.PlayWorldSound("Sounds/Abilities/sfx_ability_tonguePull");
                    Petter.Render.RenderLayer = Pettee.Render.RenderLayer - 1;
                    Petter.DirectMoveTo(Pettee.CurrentCell, 0, true, true, true);

                    consumer.Consume(Petter);
                    consumer.Active = false;

                    return Petter.IsNowhere();
                }
                consumer.Active = false;
            }
            return false;
        }
        public bool SwallowWhole(GameObject Petter)
        {
            return SwallowWhole(Petter, ParentObject, ConsumeFloatMessage);
        }

        public override bool AllowStaticRegistration()
        {
            return true;
        }
        public override void Register(GameObject Object, IEventRegistrar Registrar)
        {
            Registrar.Register(GetInventoryActionsEvent.ID, EventOrder.EXTREMELY_LATE + EventOrder.EXTREMELY_LATE);
            Registrar.Register(InventoryActionEvent.ID, EventOrder.EXTREMELY_EARLY + EventOrder.EXTREMELY_EARLY);
            base.Register(Object, Registrar);
        }
        public override bool WantEvent(int ID, int cascade)
        {
            return base.WantEvent(ID, cascade)
                || ID == AfterObjectCreatedEvent.ID
                || ID == AfterPetEvent.ID;
        }
        public override bool HandleEvent(AfterObjectCreatedEvent E)
        {
            if (E.Object == ParentObject)
            {
                Pettable pettable = ParentObject.RequirePart<Pettable>();
                pettable.OnlyAllowIfLiked = false;
                pettable.MaxAIDistance = 2;
                ParentObject.SetIntProperty("PetGoalChance", 2);
                ParentObject.SetIntProperty("PetGoalWait", 200);
                ParentObject.SetStringProperty("PreferChatToPet", "He will literally eat you");
            }
            return base.HandleEvent(E);
        }
        public override bool HandleEvent(AfterPetEvent E)
        {
            if (E.Actor != ParentObject)
            {
                MetricsManager.LogModInfo(ThisMod, $"AfterPetEvent");
                if (!Warn(E.Actor))
                {
                    bool allowSecondPerson = Grammar.AllowSecondPerson;
                    Grammar.AllowSecondPerson = false;
                    string message = GameText.VariableReplace(AngerMessage, ParentObject, E.Actor);
                    Grammar.AllowSecondPerson = allowSecondPerson;
                    EmitMessage(message, FromDialog: false, UsePopup: E.Actor.IsPlayerControlled() || ParentObject.IsPlayerControlled());

                    if (!SwallowWhole(E.Actor))
                    {
                        if (ParentObject.PartyLeader == E.Actor)
                        {
                            ParentObject.PartyLeader = null;
                        }
                        ParentObject.AddOpinion<OpinionGoad>(E.Actor);
                        ParentObject.Target = E.Actor;
                    }
                    return false;
                }
            }
            return base.HandleEvent(E);
        }
        public override bool HandleEvent(GetInventoryActionsEvent E)
        {
            if (E.Actions.ContainsKey("Pet") && E.Object == E.Actor && (E.Object == ParentObject || E.Actor == ParentObject))
            {
                E.Actions.Remove("Pet");
            }
            return base.HandleEvent(E);
        }
        public override bool HandleEvent(InventoryActionEvent E)
        {
            if (E.Command == "Pet" && E.Actor != null && !E.Actor.IsPlayerControlled())
            {
                int intMod = -999;
                if (E.Actor.HasStat("Intelligence"))
                {
                    intMod = E.Actor.StatMod("Intelligence");
                }
                if (HasWarned(E.Actor) && E.Actor.Brain != null && intMod.in10())
                {
                    int petGoalWeight = ParentObject.GetIntProperty("PetGoalWait", Pettable.DEFAULT_PET_GOAL_WAIT);
                    E.Actor.SetIntProperty("AIPetWait", petGoalWeight);

                    string failMessage = GameText.VariableReplace("=object.T= =verb:consider= petting =subject.Name=, but changes =object.its= mind recalling =object.Name's= warning.", ParentObject, E.Actor);

                    E.Actor.ShowFailure(failMessage);

                    E.RequestInterfaceExit();
                    return false;
                }
                else
                {
                    string failMessage = GameText.VariableReplace("=object.T= =verb:fail= to heed =object.Name's= warning.", ParentObject, E.Actor);
                    E.Actor.ShowFailure(failMessage);
                }
            }
            return base.HandleEvent(E);
        }
    }
}
