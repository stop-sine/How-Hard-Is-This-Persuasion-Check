using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Noggog.StructuredStrings;
using Microsoft.VisualBasic;
using DynamicData;
using System.Diagnostics;
using OneOf.Types;
using static Mutagen.Bethesda.Skyrim.Condition;
using Noggog;
using Noggog.Streams;
using FluentResults;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Exceptions;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Permissions;
using System.Data;
using System.Reflection;

namespace HowHardIsThisPersuasionCheck
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "HHITPC.esp")
                .Run(args);
        }

        public static HashSet<IFormLinkGetter<IGlobalGetter>> GlobFormLinks()
        {
            var globFormLinks = new HashSet<IFormLinkGetter<IGlobalGetter>>
            {
                (FormLink<IGlobalGetter>)FormKey.Factory("0D16A3:Skyrim.esm"),
                (FormLink<IGlobalGetter>)FormKey.Factory("0D16A4:Skyrim.esm"),
                (FormLink<IGlobalGetter>)FormKey.Factory("0D16A5:Skyrim.esm"),
                (FormLink<IGlobalGetter>)FormKey.Factory("0D1943:Skyrim.esm"),
                (FormLink<IGlobalGetter>)FormKey.Factory("0D1944:Skyrim.esm")
            };
            return globFormLinks;
        }

        public static FormLink<IItemOrListGetter> AmuletFormLink()
        {
            return new FormLink<IItemOrListGetter>(FormKey.Factory("0F759C:Skyrim.esm"));
        }


        public static bool IsPersuasionDialog(IDialogTopicGetter dial)
        {
            foreach (var info in dial.Responses)
            {
                if (HasPersuasion(info))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool HasPrompt(IDialogResponses info)
        {
            if (info.Prompt is not null)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static bool HasPersuasionPrompt(IDialogResponses info)
        {
            if (HasPrompt(info) && info.Prompt!.String!.Contains("Persuasion"))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static string? GetDifficulty(FormLink<IGlobalGetter> formLink, ILinkCache<ISkyrimMod, ISkyrimModGetter> cache)
        {
            var edid = formLink.Resolve(cache).EditorID;
            if (edid is "SpeechVeryEasy")
            {
                return "Novice";
            }
            else if (edid is "SpeechEasy")
            {
                return "Apprentice";
            }
            else if (edid is "SpeechAverage")
            {
                return "Adept";
            }
            else if (edid is "SpeechHard")
            {
                return "Expert";
            }
            else if (edid is "SpeechVeryHard")
            {
                return "Master";
            }
            else
            {
                return null;
            }
        }

        public static void ConsolePrint(ILinkCache<ISkyrimMod, ISkyrimModGetter> cache, HashSet<IFormLinkGetter<IDialogTopicGetter>> container)
        {
            foreach (var record in container)
            {
                Console.WriteLine(record.Resolve(cache).EditorID);
            }
        }

        public static HashSet<IFormLinkGetter<IDialogTopicGetter>> GetRecords(IEnumerable<IDialogTopicGetter> records)
        {
            var scopedRecords = new HashSet<IFormLinkGetter<IDialogTopicGetter>>();
            foreach (var record in records)
            {
                scopedRecords.Add((FormLink<IDialogTopicGetter>)record.FormKey);
            }
            return scopedRecords;
        }

        public static HashSet<IFormLinkGetter<IDialogTopicGetter>> GetRecords(HashSet<IFormLinkGetter<IDialogTopicGetter>> records, ILinkCache<ISkyrimMod, ISkyrimModGetter> cache)
        {
            var scopedRecords = new HashSet<IFormLinkGetter<IDialogTopicGetter>>();
            foreach (var link in records)
            {
                var record = link.Resolve(cache);
                if (IsPersuasionDialog(record))
                {
                    scopedRecords.Add((FormLink<IDialogTopicGetter>)record.FormKey);
                }
                
            }
            return scopedRecords;
        }

        public static void PatchPrompt(IDialogResponses info)
        {

        }

        // public static void PatchAmulet(IDialogResponses info, ILinkCache<ISkyrimMod, ISkyrimModGetter> cache)
        // {
        //     if (HasPersuasionCondition(info) && !HasAmuletCondition(info))
        //     {
        //         var formLink = (FormLink<IDialogResponses>)FormKey.Factory("0D16A3:Skyrim.esm");
        //         var tempInfo = formLink.ResolveAll(cache).Last();
        //         info.Conditions.Add(tempInfo.Conditions[-1].DeepCopy());
        //     }
        //     else if (HasPersuasionCondition(info, true) && !HasAmuletCondition(info, true))
        //     {
        //         var formLink = (FormLink<IDialogResponses>)FormKey.Factory("0D16A3:Skyrim.esm");
        //         var tempInfo = formLink.ResolveAll(cache).Last();
        //         info.Conditions.Add(tempInfo.Conditions[-1].DeepCopy());
        //         info.Conditions.Last().CompareOperator = CompareOperator.NotEqualTo;
        //     }
        // }

        public static void PatchSpeechReference(ICondition condition)
        {
            Console.WriteLine(condition.Data.RunOnType.ToString());
        }

        public static void PatchRecords()
        {

        }

        public static bool MissingPNAM(IDialogTopicGetter record)
        {
            foreach (var subrecord in record.Responses)
            {
                if (subrecord.PreviousDialog.IsNull &&  record.Responses.IndexOf(subrecord) is not 0)
                {
                    return true;
                }
            }
            return false;
        }

        public static void MissingPNAM(IDialogTopic record)
        {
            foreach (var subrecord in record.Responses)
            {
                if (subrecord.PreviousDialog.IsNull && record.Responses.IndexOf(subrecord) is not 0)
                {   
                    IDialogResponses priorSubrecord = record.Responses.ElementAt(record.Responses.IndexOf(subrecord) - 1);
                    subrecord.PreviousDialog = new FormLinkNullable<IDialogResponsesGetter>(priorSubrecord.FormKey);
                }
            }
        }

        public static void OveriddingRNAM()
        {

        }

        public static bool IsAmulet(IConditionGetter condition)
        {
            if (condition.Data is IConditionDataGetter conditionData && conditionData.Function is Function.GetEquipped)
            {
                if (conditionData is GetEquippedConditionData equipped && equipped.ItemOrList.Link.Equals(AmuletFormLink()))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsAmulet(ICondition condition)
        {
            if (condition.Data is IConditionData conditionData && conditionData.Function is Function.GetEquipped)
            {
                if (conditionData is GetEquippedConditionData equipped && equipped.ItemOrList.Link.Equals(AmuletFormLink()))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool HasAmulet(IDialogResponsesGetter subrecord)
        {
            foreach (var condition in subrecord.Conditions)
            {
                if (IsAmulet(condition))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool HasAmulet(IDialogResponses subrecord)
        {
            foreach (var condition in subrecord.Conditions)
            {
                if (IsAmulet(condition))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsPersuasion(IConditionGetter condition)
        {
            if (condition.DeepCopy() is ConditionGlobal global && GlobFormLinks().Contains(global.ComparisonValue))
            {
                return true;
            }
            return false;
        }

        public static bool IsPersuasion(ICondition condition)
        {
            if (condition is ConditionGlobal global && GlobFormLinks().Contains(global.ComparisonValue))
            {
                return true;
            }
            return false;
        }

        public static bool HasPersuasion(IDialogResponsesGetter info)
        {
            foreach (IConditionGetter condition in info.Conditions)
            {
                if (IsPersuasion(condition))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool HasPersuasion(IDialogResponses info)
        {
            foreach (ICondition condition in info.Conditions)
            {
                if (IsPersuasion(condition))
                {
                    return true;
                }
            }
            return false;
        }



        public static HashSet<IFormLinkGetter<IDialogTopicGetter>> GetBrokenRecords(HashSet<IFormLinkGetter<IDialogTopicGetter>> records, ILinkCache<ISkyrimMod, ISkyrimModGetter> cache)
        {
            var brokenRecords = new HashSet<IFormLinkGetter<IDialogTopicGetter>>();
            foreach (var link in records)
            {
                var record = link.Resolve(cache);
                foreach (var info in record.Responses)
                {

                }


            }
            return brokenRecords;
        }



        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            //Get Dialog Topic (DIAL) records from load order
            var dialogRecords = GetRecords(state.LoadOrder.PriorityOrder.DialogTopic().WinningOverrides());
            Console.WriteLine($"Found {dialogRecords.Count} DIAL records in load order");
            //Get records containing persuasion checks
            var persuasionRecords = GetRecords(dialogRecords, state.LinkCache);
            Console.WriteLine($"Found {persuasionRecords.Count} records with persuasion checks");
            //Get records missing conditions or responses
            var brokenRecords = GetBrokenRecords(persuasionRecords, state.LinkCache);
            Console.WriteLine($"Found {brokenRecords.Count} records with missing conditions or responses");



            //Print record Editor IDs to console

            //ConsolePrint(state.LinkCache, dialogRecords);
            //Scope broken records
            foreach (var link in persuasionRecords)
            {
                var record = link.Resolve(state.LinkCache);
                //Console.WriteLine(record.Responses.Count);
            }
            //Iterate through records to be patched
            foreach (var record in persuasionRecords)
            {
                //Add record to patch
                var dial = (IDialogTopic)state.PatchMod.DialogTopics.GetOrAddAsOverride(record.Resolve(state.LinkCache));
                //Deep copy INFO subrecords from immutable object
                //dial.Responses.Clear();
                //foreach (var info in record.Resolve(state.LinkCache).Responses)
                //{
                //dial.Responses.Add(info.DeepCopy());
                //}
                //PatchPreviousDialogs(dial.Responses);
                //Iterate through INFO subrecords
                //Console.WriteLine(dial.Responses.Count);
                foreach (IDialogResponses info in dial.Responses.Cast<IDialogResponses>())
                {
                    Console.WriteLine("blah");
                    foreach (var condition in info.Conditions)
                    {
                        Console.WriteLine("bleh");
                        if (IsPersuasion(condition))
                        {
                            Console.WriteLine(condition.Data.RunOnType.ToString());
                        }
                        //if (condition is ConditionGlobal)
                        //{
                        //Console.WriteLine(condition.Data.RunOnType);
                        //var globalCondition = (ConditionGlobal)condition;
                        //var conditionData = (IConditionData)condition.Data;
                        //Console.WriteLine(globalCondition.ComparisonValue.Resolve(state.LinkCache));
                        //Console.WriteLine(conditionData.Function);
                        //}
                    }
                }
            }
        }
    }
}
                    //Iterate through Conditions for speech difficulty
                    //foreach (var info in dialogTopic.Responses)
                    //{
                        //foreach (var condition in info.Conditions)
                        //{
                            //var conditionData = condition.Data as IConditionDataGetter;
                            //var parametersGetter = conditionData as IConditionParametersGetter;
                            //if (conditionData?.Function is Function.GetActorValue)
                            //{
                                //var conditionGlobalGetter = condition as IConditionGlobalGetter;
                                //(condition as IConditionGlobalGetter)?.ComparisonValue.ResolveIdentifier(linkcache)?.Contains;
                                //Console.WriteLine(conditionGlobalGetter?.CompareOperator);
                                //Console.WriteLine(conditionGlobalGetter?.ComparisonValue);
                                //Console.WriteLine(conditionGlobalGetter?.ComparisonValue.ResolveIdentifier(linkcache));
                                //var actorValueData = conditionData as GetActorValueConditionData;
                                //Console.WriteLine(actorValueData?.ActorValue);
                                //parametersGetter
                            //}
                        //}
                        
                    //}

//var conditionData = condition.Data as GetIsIDConditionData;
//Console.WriteLine(conditionData?.Function);
//Console.WriteLine(parametersGetter?.Object.Link.TryResolve(linkcache));
//Console.WriteLine(conditionData?.Object.Link.ResolveIdentifier(linkcache));
//IFormLinkGetter<INpcGetter> link = conditionData.Object.Link;


//if (.TryResolve(myLink, out var record))
//{
//Console.WriteLine($"Found the npc! {npc.EditorID}");
//}
//parametersGetter.UsePackageData
//var npcGetter = parametersGetter as INpcGetter;
//Console.WriteLine(npcGetter?.Type);
//var parameter1 = parametersGetter.Parameter1 as INpcGetter;
//Console.WriteLine(parameter1);
//Console.WriteLine(parametersGetter.Parameter1);
//parametersGetter.Parameter1
//Console.WriteLine(parametersGetter.Parameter2);
//Console.WriteLine(parametersGetter.StringParameter1);
//Console.WriteLine(parametersGetter.StringParameter2);


//Iterate through INFO subrecords
//foreach (var info in dialogtopicGetter.Responses)
//{
//}
//var dialogTopic = state.PatchMod.DialogTopics.GetOrAddAsOverride(dialogtopicGetter);
//if (dialogTopic.Name != null)
//{
//dialogTopic.Name.String = "af";
//}

//foreach (var info in dialogtopicGetter.Responses)
//{

//}
