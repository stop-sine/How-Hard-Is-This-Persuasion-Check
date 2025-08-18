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
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins.Implicit;

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

        public static HashSet<IFormLinkGetter<IGlobalGetter>> GetSpeech()
        {
            var speech = new HashSet<IFormLinkGetter<IGlobalGetter>>
            {
                Skyrim.Global.SpeechVeryEasy,
                Skyrim.Global.SpeechEasy,
                Skyrim.Global.SpeechAverage,
                Skyrim.Global.SpeechHard,
                Skyrim.Global.SpeechVeryHard
            };
            return speech;
        }

        public static string AssignDifficulty(IFormLink<IGlobalGetter> formLink)
        {
            
            if (formLink.Equals(Skyrim.Global.SpeechVeryEasy))
            {
                return "Novice";
            }
            else if (formLink.Equals(Skyrim.Global.SpeechEasy))
            {
                return "Apprentice";
            }
            else if (formLink.Equals(Skyrim.Global.SpeechAverage))
            {
                return "Adept";
            }
            else if (formLink.Equals(Skyrim.Global.SpeechHard))
            {
                return "Expert";
            }
            else if (formLink.Equals(Skyrim.Global.SpeechVeryHard))
            {
                return "Master";
            }
            else
            {
                return "";
            }
        }

        public static void ConsolePrint(ILinkCache<ISkyrimMod, ISkyrimModGetter> cache, HashSet<IFormLinkGetter<IDialogTopicGetter>> container)
        {
            foreach (var record in container)
            {
                Console.WriteLine(record.Resolve(cache).EditorID);
            }
        }

        public static bool TryResolveAmulet(IDialogResponses info, out ICondition? amuletCondition)
        {
            amuletCondition = null;
            foreach (ICondition condition in info.Conditions)
            {
                if (condition.Data is IConditionData conditionData &&
                conditionData.Function is Function.GetEquipped &&
                conditionData is GetEquippedConditionData equipped &&
                equipped.ItemOrList.Link.Equals(Skyrim.FormList.TGAmuletofArticulationList))
                {
                    amuletCondition = condition;
                    return true;
                }
            }
            return false;
        }

        public static bool TryResolveAmulet(IDialogResponsesGetter info, out IConditionGetter? amuletCondition)
        {
            amuletCondition = null;
            foreach (IConditionGetter condition in info.Conditions)
            {
                if (condition.DeepCopy().Data is IConditionData conditionData &&
                conditionData.Function is Function.GetEquipped &&
                conditionData is GetEquippedConditionData equipped &&
                equipped.ItemOrList.Link.Equals(Skyrim.FormList.TGAmuletofArticulationList))
                {
                    amuletCondition = condition;
                    return true;
                }
            }
            return false;
        }

        public static bool TryResolvePersuasion(IDialogResponses info, out ICondition? persuasionCondition)
        {
            persuasionCondition = null;
            foreach (ICondition condition in info.Conditions)
            {
                if (condition.DeepCopy() is ConditionGlobal global && GetSpeech().Contains(global.ComparisonValue))
                {
                    persuasionCondition = condition;
                    return true;
                }
            }
            return false;
        }

        public static bool TryResolvePersuasion(IDialogResponsesGetter info, out IConditionGetter? persuasionCondition)
        {
            persuasionCondition = null;
            foreach (IConditionGetter condition in info.Conditions)
            {
                if (condition.DeepCopy() is ConditionGlobal global && GetSpeech().Contains(global.ComparisonValue))
                {
                    persuasionCondition = condition;
                    return true;
                }
            }
            return false;
        }

        public static bool TryResolveName(IDialogTopic record, out string name)
        {
            name = "";
            if (record.Name is not null && !record.Name.String.IsNullOrEmpty())
            {
                name = record.Name!;
                return true;
            }
            return false;
        }

        public static bool TryResolveName(IDialogTopicGetter recordGetter, out string name)
        {
            name = "";
            IDialogTopic record = recordGetter.DeepCopy();
            if (record.Name is not null && !record.Name.String.IsNullOrEmpty())
            {
                name = record.Name.String;
                return true;
            }
            return false;
        }

        public static bool TryResolvePrompt(IDialogResponses subrecord, out string prompt)
        {
            prompt = "";
            if (subrecord.Prompt is not null)
            {
                prompt = subrecord.Prompt.String!;
                return true;
            }
            return false;
        }

        public static bool TryResolvePrompt(IDialogResponsesGetter subrecordGetter, out string prompt)
        {
            prompt = "";
            IDialogResponses subrecord = subrecordGetter.DeepCopy();
            if (subrecord.Prompt is not null)
            {
                prompt = subrecord.Prompt.String!;
                return true;
            }
            return false;
        }

        public static bool TryResolveDifficulty(ICondition condition, out string difficulty)
        {
            difficulty = "";
            if (condition is ConditionGlobal global && GetSpeech().Contains(global.ComparisonValue))
            {
                difficulty = AssignDifficulty(global.ComparisonValue);
                return true;
            }
            return false;
        }

        public static bool TryResolveDifficulty(IConditionGetter condition, out string difficulty)
        {
            difficulty = "";
            if (condition.DeepCopy() is ConditionGlobal global && GetSpeech().Contains(global.ComparisonValue))
            {
                difficulty = AssignDifficulty(global.ComparisonValue);
                return true;
            }
            return false;
        }

        public static IDialogResponses CopySubrecord(IDialogTopic record, IDialogResponsesGetter subrecordGetter)
        {
            if (!record.Responses.Any() || !record.Responses.Last().FormKey.Equals(subrecordGetter.FormKey))
            {
                record.Responses.Add(subrecordGetter.DeepCopy());
            }
            return record.Responses.Last();
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            //Declare container for Dialog Topic (DIAL) records
            var dialogRecords = new HashSet<IFormLinkGetter<IDialogTopicGetter>>();
            //Iterate through winning DIAL records in load order
            foreach (var record in state.LoadOrder.PriorityOrder.DialogTopic().WinningOverrides())
            {
                //Assign record's FormLink to DIAL container
                dialogRecords.Add((FormLink<IDialogTopicGetter>)record.FormKey);
            }
            //Print number of DIAL records in load order
            Console.WriteLine($"Found {dialogRecords.Count} DIAL records in load order");
            //Declare container for DIAL records to be patched
            var patchedRecords = new HashSet<IFormLinkGetter<IDialogTopicGetter>>();
            //Iterate through DIAL records
            foreach (var link in dialogRecords)
            {
                //Resolve and assign record's FormLink
                var record = link.Resolve(state.LinkCache);
                //Iterate through INFO subrecords in record
                foreach (var subrecord in record.Responses)
                {
                    //Check if subrecord contains a persuasion check
                    if (TryResolvePersuasion(subrecord, out _))
                    {
                        //Assign parent record's FormLink to container
                        patchedRecords.Add(link);
                    }
                }
            }
            //Print number of records with persuasion checks
            Console.WriteLine($"Found {patchedRecords.Count} records with persuasion checks");
            //Print record Editor IDs to console
            ConsolePrint(state.LinkCache, patchedRecords);
            //Iterate through records to be patched
            foreach (var link in patchedRecords)
            {
                //Declare container for INFO subrecords
                var grup = new HashSet<IDialogResponsesGetter>();
                //Resolve FormLink
                var recordGetter = link.Resolve(state.LinkCache);
                //Add record to patch
                IDialogTopic record = state.PatchMod.DialogTopics.GetOrAddAsOverride(recordGetter);
                //Container for persuasion checks
                var checks = new List<string>();
                foreach (IDialogResponsesGetter subrecordGetter in recordGetter.Responses)
                {
                    foreach (IConditionGetter conditionGetter in subrecordGetter.Conditions)
                    {
                        if (TryResolveDifficulty(conditionGetter, out var difficulty) && !difficulty.IsNullOrEmpty())
                        {
                            var subrecord = CopySubrecord(record, subrecordGetter);
                            ICondition condition = subrecord.Conditions[subrecordGetter.Conditions.IndexOf(conditionGetter)];
                            checks.Add(difficulty);
                            condition.Data.RunOnType = RunOnType.Reference;
                            condition.Data.Reference = Skyrim.PlayerRef;
                            if (condition.Flags is not Flag.OR)
                            {
                                condition.Flags = Flag.OR;
                            }
                        }
                    }
                    if (TryResolvePersuasion(subrecordGetter, out var _) && !TryResolveAmulet(subrecordGetter, out _))
                    {
                        var subrecord = CopySubrecord(record, subrecordGetter);
                        Condition amuletCondition = new ConditionFloat
                        {
                            Data = new GetEquippedConditionData
                            {
                                RunOnType = RunOnType.Reference,
                                Reference = Skyrim.PlayerRef,
                                ItemOrList = {
                                    Link = {
                                        FormKey = Skyrim.FormList.TGAmuletofArticulationList.FormKey
                                    }
                                },
                            },
                            CompareOperator = CompareOperator.EqualTo,
                            ComparisonValue = 1
                        };
                        TryResolvePersuasion(subrecord, out var persuasionCondition);
                        if (subrecord.Conditions.Last() == persuasionCondition)
                        {
                            subrecord.Conditions.Add(amuletCondition);
                        }
                        else
                        {
                            subrecord.Conditions.Insert(subrecord.Conditions.IndexOf(persuasionCondition) + 1, amuletCondition);
                        }
                    }
                    if (TryResolvePrompt(subrecordGetter, out var prompt) && prompt.Contains("(Persuade)"))
                    {
                        var subrecord = CopySubrecord(record, subrecordGetter);
                        subrecord.Prompt!.String = prompt.Replace("(Persuade)", $"(Persuade: {checks.Last()})");
                    }
                    if (!TryResolvePrompt(subrecordGetter, out var _) && TryResolvePersuasion(subrecordGetter, out var _))
                    {
                        var subrecord = CopySubrecord(record, subrecordGetter);
                        var temp = record.Name!.String!;
                        subrecord.Prompt = temp.Replace("(Persuade)", $"(Persuade: {checks.Last()})");
                    }
                    if (recordGetter.FormKey.Equals(Skyrim.DialogTopic.MS05PoemVerse2Evil) || recordGetter.FormKey.Equals(Skyrim.DialogTopic.MG03CallerBookPersuade))
                    {
                        var subrecord = CopySubrecord(record, subrecordGetter);
                        subrecord.Prompt?.Clear();
                    }
                    if (subrecordGetter.PreviousDialog is null && !recordGetter.Responses[0].Equals(subrecordGetter))
                    {
                        var subrecord = CopySubrecord(record, subrecordGetter);
                        IDialogResponsesGetter priorSubrecord = recordGetter.Responses[recordGetter.Responses.IndexOf(subrecordGetter) - 1];
                        subrecord.PreviousDialog = new FormLinkNullable<IDialogResponsesGetter>(priorSubrecord);
                    }
                    if (recordGetter.FormKey.Equals(Skyrim.DialogTopic.DA15Convince))
                    {
                        var subrecord = CopySubrecord(record, subrecordGetter);
                        subrecord.Conditions.First().Data.Reference = Skyrim.Npc.FalkFirebeard;
                    }
                }
                if (TryResolveName(recordGetter, out var name) && name.Contains("(Persuade)"))
                {
                    record.Name!.String = name.Replace("(Persuade)", $"(Persuade: {checks.Last()})");
                }
            }    
        }
    }
}
