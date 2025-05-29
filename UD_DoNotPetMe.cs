using HarmonyLib;

using System;
using System.Collections.Generic;

using XRL.Language;
using XRL.World;
using XRL.World.Parts;
using XRL.World.Parts.Mutation;

namespace XRL.World.Parts
{
    [HarmonyPatch]
    [Serializable]
    public class UD_DoNotPetMe : IScribedPart
    {
        public List<string> HasWarned = new();
        public string WarnMessage;
        public string WarnFilter;
        public string AngerMessage;
        public string ConsumeFloatMessage;

        public UD_DoNotPetMe() 
        {
            WarnMessage = "Best not be doing that gain!";
            AngerMessage = "YOU WERE WARNED";
        }

        public override bool AllowStaticRegistration()
        {
            return true;
        }
        public override void Register(GameObject Object, IEventRegistrar Registrar)
        {
            Registrar.Register(GetInventoryActionsEvent.ID, EventOrder.EXTREMELY_LATE + EventOrder.EXTREMELY_LATE);
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
                pettable.MaxAIDistance = 1;
                ParentObject.SetIntProperty("PetGoalWait", 999999);
                ParentObject.SetStringProperty("PreferChatToPet", "He will literally eat you");
            }
            return base.HandleEvent(E);
        }
        public override bool HandleEvent(AfterPetEvent E)
        {
            if (E.Actor != ParentObject)
            {
                if (!HasWarned.Contains(E.Actor.ID))
                {
                    HasWarned.Add(E.Actor.ID);
                    bool allowSecondPerson = Grammar.AllowSecondPerson;
                    Grammar.AllowSecondPerson = false;
                    string message = GameText.VariableReplace(WarnMessage, ParentObject, E.Actor);
                    Grammar.AllowSecondPerson = allowSecondPerson;
                    EmitMessage(TextFilters.Filter(message, WarnFilter), ' ', FromDialog: false, E.Actor.IsPlayerControlled() || ParentObject.IsPlayerControlled());
                }
                else
                {
                    bool allowSecondPerson = Grammar.AllowSecondPerson;
                    Grammar.AllowSecondPerson = false;
                    string message = GameText.VariableReplace(AngerMessage, ParentObject, E.Actor);
                    Grammar.AllowSecondPerson = allowSecondPerson;
                    EmitMessage(message, ' ', FromDialog: false, E.Actor.IsPlayerControlled() || ParentObject.IsPlayerControlled());

                    Consumer consumer = ParentObject.RequirePart<Consumer>();
                    consumer.WeightThresholdPercentage = 49999;
                    consumer.Message = "{{R|=subject.T= =verb:swallow= =object.Name= whole for petting =pronouns.objective= too many times!}}";
                    consumer.FloatMessage = ConsumeFloatMessage;

                    if (consumer.CanConsume(E.Actor) && !consumer.ShouldIgnore(E.Actor))
                    {
                        CombatJuiceEntryPunch punch = CombatJuice.punch(ParentObject, E.Actor, 0.1f);
                        CombatJuiceManager.enqueueEntry(punch, async: true);

                        ParentObject.PlayWorldSound("Sounds/Abilities/sfx_ability_tonguePull");
                        E.Actor.Render.RenderLayer = E.Object.Render.RenderLayer - 1;
                        E.Actor.DirectMoveTo(E.Object.CurrentCell, 0, true, true, true);
                        consumer.Consume(E.Actor);
                        return false;
                    }
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
    }
}

/*
namespace UD_Pettable_Mak
{
    [HarmonyPatch]
    public static class Pettable_Patches
    {
        [HarmonyPatch(
            declaringType: typeof(Pettable),
            methodName: nameof(Pettable.CanPet),
            argumentTypes: new Type[] { typeof(GameObject), typeof(string) },
            argumentVariations: new ArgumentType[] { ArgumentType.Normal, ArgumentType.Out })]
        [HarmonyPostfix]
        public static void CanPet_CantPetSelf_Postfix(ref bool __result, ref Pettable __instance, GameObject Actor, out string FailureMessage)
        {
            Pettable @this = __instance;
            FailureMessage = null;
            if (Actor == @this.ParentObject)
            {
                __result = false;
                FailureMessage = "=subject.T= cannot pet =pronouns.reflexive=!";
            }
        }
    }
}
*/
