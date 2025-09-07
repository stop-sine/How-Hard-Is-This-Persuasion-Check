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
using System.Linq;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins.Implicit;
using Noggog.StructuredStrings.CSharp;
using CommandLine;
using System.ComponentModel.Design;
using Mutagen.Bethesda.Plugins.Analysis;

namespace HowHardIsThisPersuasionCheck
{
    public class Program
    {
        public static readonly List<FormLink<IGlobalGetter>> SpeechGlobals =
        [
            Skyrim.Global.SpeechVeryEasy,
            Skyrim.Global.SpeechEasy,
            Skyrim.Global.SpeechAverage,
            Skyrim.Global.SpeechHard,
            Skyrim.Global.SpeechVeryHard
        ];

        public static readonly Dictionary<IFormLink<IGlobalGetter>, string> SpeechValues = new()
        {
            {Skyrim.Global.SpeechVeryEasy, "Novice"},
            {Skyrim.Global.SpeechEasy, "Apprentice"},
            {Skyrim.Global.SpeechAverage, "Adept"},
            {Skyrim.Global.SpeechHard, "Expert"},
            {Skyrim.Global.SpeechVeryHard, "Master"}
        };

        public static readonly List<FormLink<IDialogTopicGetter>> BrokenRecords =
        [
            Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed3,
            Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed2,
            Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed1,
            Skyrim.DialogTopic.DA11IntroVerulusPersuade,
            Skyrim.DialogTopic.DB01MiscLoreiusHelpCiceroResponseb,
            Skyrim.DialogTopic.DB02Captive3Persuade,
            Skyrim.DialogTopic.DB02Captive2Persuade,
            Skyrim.DialogTopic.DB02Captive1Persuade,
            Skyrim.DialogTopic.DialogueWhiterunGuardGateStopIntimidate,
            Skyrim.DialogTopic.DialogueWhiterunGuardGateStopBribe,
            Skyrim.DialogTopic.DialogueWhiterunGuardGateStopPersuade,
            Skyrim.DialogTopic.DA03StartLodBranchPersuadeTopic,
            //FormKey.Factory("000087D:HearthFires.esm")
        ];

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "HHITPC.esp")
                .Run(args);
        }

        public static string GetSpeechValue(Condition condition)
        {
            return SpeechValues[condition.Cast<ConditionGlobal>().ComparisonValue];
        }

        public static string GetSpeechValue(IConditionGetter condition)
        {
            return SpeechValues[condition.Cast<IConditionGlobal>().ComparisonValue];
        }

        public static IConditionGetter? GetSpeechCondition(IDialogResponsesGetter? info)
        {
            return info?.Conditions.FirstOrDefault(c => c is IConditionGlobalGetter global && SpeechGlobals.Contains(global.ComparisonValue.FormKey));
        }

        public static ConditionGlobal ConstructSpeech(IFormLink<IGlobalGetter> speechDifficulty)
        {
            var speech = new ConditionGlobal
            {
                Data = new GetActorValueConditionData { RunOnType = RunOnType.Reference, Reference = Skyrim.PlayerRef, ActorValue = ActorValue.Speech },
                CompareOperator = CompareOperator.GreaterThan,
                ComparisonValue = speechDifficulty
            };
            return speech;
        }

        public static ConditionFloat ConstructAmulet(bool anti = default)
        {
            var amulet = new ConditionFloat
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
            if (anti)
                amulet.CompareOperator = CompareOperator.NotEqualTo;
            return amulet;
        }

        public static bool SpeechConditionFilter(IConditionGetter condition)
        {
            return condition is IConditionGlobalGetter { ComparisonValue: var value }
            && SpeechGlobals.Contains(value);
        }

        public static bool SpeechCheckFilter(IDialogResponsesGetter subrecord)
        {
            return subrecord.Conditions.Any(SpeechConditionFilter);
        }

        public static bool SpeechPromptFilter(IDialogResponsesGetter subrecord)
        {
            return subrecord.Prompt?.String?.Contains("(Persuade)") ?? false;
        }

        public static bool SpeechNameFilter(IDialogTopicGetter record)
        {
            return record.Name?.String?.Contains("(Persuade)") ?? false;
        }

        public static bool AmuletConditionFilter(IConditionGetter condition)
        {
            return condition is IConditionFloatGetter { Data: IGetEquippedConditionDataGetter { ItemOrList.Link: var link } } && link.Equals(Skyrim.FormList.TGAmuletofArticulationList);
        }

        public static bool AmuletFilter(IDialogResponsesGetter subrecord)
        {
            return subrecord.Conditions.Any(AmuletConditionFilter);
        }

        public static bool DifferentSpeechChecksFilter(IDialogTopicGetter record)
        {
            var conditions = record.Responses.Where(SpeechCheckFilter).Select(GetSpeechCondition);
            var values = conditions.Select(GetSpeechValue!);
            return values.Distinct().Count() > 1;

        }

        public static bool RecordFilter(IDialogTopicGetter record)
        {
            return SpeechNameFilter(record)
            || record.Responses.Any(SpeechPromptFilter)
            || record.Responses.Any(SpeechCheckFilter)
            || BrokenRecords.Contains(record.ToLink());
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var cache = state.LinkCache;
            var patchMod = state.PatchMod;
            var allRecords = state.LoadOrder.PriorityOrder.DialogTopic().WinningOverrides();
            var records = allRecords.Where(RecordFilter).ToList();
            Console.WriteLine($"Found {records.Count} records to be patched");
            //records.ForEach(record => Console.WriteLine(record.EditorID));
            foreach (var recordGetter in records)
            {
                Console.WriteLine(recordGetter.EditorID);
                Console.WriteLine(recordGetter.FormKey);
                var record = patchMod.DialogTopics.GetOrAddAsOverride(recordGetter);
                var name = record.Name?.String;
                record.Responses.Add(recordGetter.Responses.Select(r => r.DeepCopy()));
                var grup = record.Responses;
                if (record.Equals(Skyrim.DialogTopic.MG04MirabelleAugurInfoBranchTopic))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("04FA11:Skyrim.esm"));
                    grup.Insert(grup.IndexOf(baseResponse!) + 1, new DialogResponses(patchMod)
                    {
                        Conditions = [baseResponse!.Conditions[0], baseResponse.Conditions[1]],
                        Flags = new DialogResponseFlags() { Flags = DialogResponses.Flag.Goodbye },
                        ResponseData = FormKey.Factory("0E0CC4:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>()
                    });
                }
                if (record.Equals(Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed3))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0556FA:Skyrim.esm"));
                    record.Name = baseResponse!.Prompt;
                    baseResponse.Prompt = null;
                    baseResponse.Conditions.Add(ConstructSpeech(Skyrim.Global.SpeechVeryEasy));
                    grup.Insert(grup.IndexOf(baseResponse) + 1, new DialogResponses(patchMod)
                    {
                        Conditions = [baseResponse.Conditions[0], baseResponse.Conditions[1]],
                        ResponseData = FormKey.Factory("0E0CC4:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        LinkTo = [
                            Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed1,
                            Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed2,
                            Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed3
                        ]
                    });
                }
                if (record.Equals(Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed2))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0556F8:Skyrim.esm"));
                    record.Name = baseResponse!.Prompt;
                    baseResponse.Prompt = null;
                    baseResponse.Conditions.Add(ConstructSpeech(Skyrim.Global.SpeechVeryEasy));
                    grup.Insert(grup.IndexOf(baseResponse) + 1, new DialogResponses(patchMod)
                    {
                        ResponseData = FormKey.Factory("0E0CC4:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        Flags = new DialogResponseFlags(),
                        Conditions = [baseResponse.Conditions[0], baseResponse.Conditions[1]],
                        LinkTo = [Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed1,
                        Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed2,
                        Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed3]
                    });
                }
                if (record.Equals(Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed1))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0556F7:Skyrim.esm"));
                    record.Name = baseResponse!.Prompt;
                    baseResponse.Prompt = null;
                    baseResponse.Conditions.Add(ConstructSpeech(Skyrim.Global.SpeechAverage));
                    grup.Insert(grup.IndexOf(baseResponse) + 1, new DialogResponses(patchMod)
                    {
                        Conditions = [baseResponse.Conditions[0], baseResponse.Conditions[1]],
                        ResponseData = FormKey.Factory("0E0CC4:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        LinkTo = [
                            Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed1,
                            Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed2,
                            Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed3
                        ]
                    });

                }
                if (record.Equals(Skyrim.DialogTopic.DA11IntroVerulusPersuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("060652:Skyrim.esm"));
                    baseResponse!.Conditions.Add(ConstructSpeech(Skyrim.Global.SpeechEasy));
                    grup.Insert(grup.IndexOf(baseResponse) + 1, new DialogResponses(patchMod)
                    {
                        Conditions = [baseResponse.Conditions[0]],
                        Flags = new DialogResponseFlags() { Flags = DialogResponses.Flag.Goodbye },
                        ResponseData = FormKey.Factory("0E0CC4:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>()
                    });
                }
                if (record.Equals(Skyrim.DialogTopic.DB01MiscLoreiusHelpCiceroResponseb))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("07DE91:Skyrim.esm"));
                    record.Name = baseResponse!.Prompt;
                    baseResponse.Prompt = null;
                    baseResponse.Conditions.Add(ConstructSpeech(Skyrim.Global.SpeechEasy));
                    grup.Insert(grup.IndexOf(baseResponse) + 1, new DialogResponses(patchMod)
                    {
                        Conditions = [baseResponse.Conditions[0]],
                        Flags = new DialogResponseFlags() { Flags = DialogResponses.Flag.Goodbye },
                        ResponseData = FormKey.Factory("0E0CC4:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        LinkTo = [
                            Skyrim.DialogTopic.DB01MiscLoreiusScrewCiceroYes,
                            Skyrim.DialogTopic.DB01MiscLoreiusScrewCiceroNo,
                            Skyrim.DialogTopic.DB01MiscLoreiusHelpCiceroResponseb
                        ]
                    });
                }
                if (record.Equals(Skyrim.DialogTopic.DB02Captive3Persuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("09DEA6:Skyrim.esm"));
                    record.Name = baseResponse!.Prompt;
                    baseResponse.Prompt = null;
                    baseResponse.Conditions.Add(ConstructSpeech(Skyrim.Global.SpeechAverage));
                    grup.Insert(grup.IndexOf(baseResponse) + 1, new DialogResponses(patchMod)
                    {
                        Conditions = [baseResponse.Conditions[0]],
                        ResponseData = FormKey.Factory("0E0CC5:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        LinkTo = [Skyrim.DialogTopic.DB02Captive3Intimidate, Skyrim.DialogTopic.DB02Captive3Persuade]
                    });
                }
                if (record.Equals(Skyrim.DialogTopic.DB02Captive2Persuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("09DEA1:Skyrim.esm"));
                    record.Name = baseResponse!.Prompt;
                    baseResponse.Prompt = null;
                    baseResponse.Conditions.Add(ConstructSpeech(Skyrim.Global.SpeechAverage));
                    grup.Insert(grup.IndexOf(baseResponse) + 1, new DialogResponses(patchMod)
                    {
                        Conditions = [baseResponse.Conditions[0]],
                        ResponseData = FormKey.Factory("0E0CC4:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        LinkTo = [Skyrim.DialogTopic.DB02Captive2Intimidate, Skyrim.DialogTopic.DB02Captive2Persuade]
                    });
                }
                if (record.Equals(Skyrim.DialogTopic.DB02Captive1Persuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("09DEA2:Skyrim.esm"));
                    record.Name = baseResponse!.Prompt;
                    baseResponse.Prompt = null;
                    baseResponse.Conditions.Add(ConstructSpeech(Skyrim.Global.SpeechAverage));
                    grup.Insert(grup.IndexOf(baseResponse) + 1, new DialogResponses(patchMod)
                    {
                        Conditions = [baseResponse.Conditions[0]],
                        ResponseData = FormKey.Factory("0E0CC3:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        LinkTo = [Skyrim.DialogTopic.DB02Captive1Intimidate, Skyrim.DialogTopic.DB02Captive1Persuade]
                    });
                }
                if (record.Equals(Skyrim.DialogTopic.DialogueWhiterunGuardGateStopIntimidate))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0D197F:Skyrim.esm"));
                    baseResponse!.Conditions.Add(new ConditionFloat
                    {
                        Data = new GetIntimidateSuccessConditionData { RunOnType = RunOnType.Subject },
                        CompareOperator = CompareOperator.EqualTo,
                        ComparisonValue = 0
                    });
                    var newResponse = new DialogResponses(patchMod)
                    {
                        VirtualMachineAdapter = baseResponse.VirtualMachineAdapter,
                        Flags = new DialogResponseFlags()
                        {
                            Flags = DialogResponses.Flag.Goodbye
                        },
                        ResponseData = FormKey.Factory("0E0CBC:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        Conditions = [baseResponse.Conditions[0], new ConditionFloat{
                        Data = new GetIntimidateSuccessConditionData { RunOnType = RunOnType.Subject }, CompareOperator = CompareOperator.EqualTo, ComparisonValue = 1}]
                    };
                    baseResponse.VirtualMachineAdapter = null;
                    grup.Insert(grup.IndexOf(baseResponse) + 1, newResponse);
                }
                if (record.Equals(Skyrim.DialogTopic.DialogueWhiterunGuardGateStopBribe))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0D197B:Skyrim.esm"));
                    baseResponse!.Conditions.Add(new ConditionFloat
                    {
                        Data = new GetBribeSuccessConditionData { RunOnType = RunOnType.Subject },
                        CompareOperator = CompareOperator.EqualTo,
                        ComparisonValue = 1
                    });
                    grup.Insert(grup.IndexOf(baseResponse) + 1, new DialogResponses(patchMod)
                    {
                        Flags = new DialogResponseFlags(),
                        ResponseData = FormKey.Factory("0E0CC4:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        Conditions = [baseResponse.Conditions[0], new ConditionFloat{
                        Data = new GetIntimidateSuccessConditionData { RunOnType = RunOnType.Subject }, CompareOperator = CompareOperator.EqualTo, ComparisonValue = 0}],
                        LinkTo = [Skyrim.DialogTopic.DialogueWhiterunGuardGateStopNote,
                        Skyrim.DialogTopic.DialogueWhiterunGuardGateStopPersuade,
                        Skyrim.DialogTopic.DialogueWhiterunGuardGateStopBribe,
                        Skyrim.DialogTopic.DialogueWhiterunGuardGateStopIntimidate,
                        Skyrim.DialogTopic.DialogueWhiterunGuardGateStopNevermind]
                    });
                }
                if (record.Equals(Skyrim.DialogTopic.DialogueWhiterunGuardGateStopPersuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0D1981:Skyrim.esm"));
                    baseResponse!.Conditions.Add(ConstructSpeech(Skyrim.Global.SpeechAverage));
                    baseResponse!.Flags!.Flags |= DialogResponses.Flag.SayOnce;
                    grup.Insert(grup.IndexOf(baseResponse) + 1, new DialogResponses(patchMod)
                    {
                        Flags = new DialogResponseFlags(),
                        LinkTo = [Skyrim.DialogTopic.DialogueWhiterunGuardGateStopNote,
                        Skyrim.DialogTopic.DialogueWhiterunGuardGateStopPersuade,
                        Skyrim.DialogTopic.DialogueWhiterunGuardGateStopBribe,
                        Skyrim.DialogTopic.DialogueWhiterunGuardGateStopIntimidate,
                        Skyrim.DialogTopic.DialogueWhiterunGuardGateStopNevermind],
                        ResponseData = FormKey.Factory("0E0CC3:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        Conditions = [baseResponse.Conditions[0]]
                    });
                }
                if (record.Equals(Skyrim.DialogTopic.DA03StartLodBranchPersuadeTopic))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0D7933:Skyrim.esm"));
                    baseResponse!.Conditions.Add(ConstructSpeech(Skyrim.Global.SpeechEasy));
                    baseResponse.VirtualMachineAdapter!.Scripts[0].Properties.Add(new ScriptObjectProperty
                    {
                        Name = "pFDS",
                        Flags = ScriptProperty.Flag.Edited,
                        Object = Skyrim.Quest.DialogueFavorGeneric
                    });
                    baseResponse.VirtualMachineAdapter!.ScriptFragments!.OnBegin = new ScriptFragment
                    {
                        ScriptName = "TIF__000D7933",
                        FragmentName = "Fragment_1"
                    };
                    var newResponse = new DialogResponses(patchMod)
                    {
                        Flags = new DialogResponseFlags(),
                        ResponseData = FormKey.Factory("0E0CC3:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        Conditions = [new ConditionFloat{
                            Data = new GetStageConditionData{
                                RunOnType = RunOnType.Subject,
                            }, CompareOperator = CompareOperator.LessThan, ComparisonValue = 10
                        },]
                    };
                    newResponse.Conditions[0].Data.Cast<GetStageConditionData>().Quest.Link.FormKey = Skyrim.Quest.DA03Start.FormKey;
                    grup.Insert(grup.IndexOf(baseResponse) + 1, newResponse);
                }
                if (record.Equals(Skyrim.DialogTopic.DialogueRiftenGateNonNorthBranchTopic02))
                    record.Name = record.Name?.String?.Replace("(Persuade)", "");

                if (name is not null && grup.Any(SpeechCheckFilter) && !DifferentSpeechChecksFilter(record) && !record.Equals(Skyrim.DialogTopic.MS09ThoraldQuestionsBranchTopic))
                {
                    var speech = grup.First(SpeechCheckFilter).Conditions.First(SpeechConditionFilter);
                    if (name.Contains("(Persuade)"))
                        record.Name = name.Replace("(Persuade)", $"(Persuade: {GetSpeechValue(speech)})");
                    else if (!name.Contains("(Intimidate)") && !name.Contains("gold)"))
                        record.Name += $" (Persuade: {GetSpeechValue(speech)})";
                }

                var speechGrup = grup.Where(SpeechCheckFilter);
                foreach (var info in grup)
                {
                    Console.WriteLine(info.FormKey);
                    if (SpeechCheckFilter(info))
                    {
                        var speech = info.Conditions.Find(SpeechConditionFilter);
                        speech!.Data.RunOnType = RunOnType.Reference;
                        speech!.Data.Reference = Skyrim.PlayerRef;
                        if (!AmuletFilter(info))
                            if (speech.CompareOperator == CompareOperator.EqualTo)
                                info.Conditions.Insert(info.Conditions.IndexOf(speech!) + 1, ConstructAmulet());
                            else
                                info.Conditions.Insert(info.Conditions.IndexOf(speech!) + 1, ConstructAmulet(true));
                        if (!(name is not null && name.Contains("(Persuade:")) && info.Prompt is not null && info.Prompt.String is not null && info.Prompt.String.Contains("(Persuade)"))
                        {
                            if (DifferentSpeechChecksFilter(record))
                            {
                                var sharedInfo = grup.Where(i => !SpeechCheckFilter(i) && i.Prompt?.String == info.Prompt?.String);
                                sharedInfo.ForEach(i => i.Prompt = i.Prompt!.String?.Replace("(Persuade)", $"(Persuade: {GetSpeechValue(speech!)})"));
                            }
                            info.Prompt.String = info.Prompt.String.Replace("(Persuade)", $"(Persuade: {GetSpeechValue(speech!)})");
                        }
                    }
                    if (info.PreviousDialog.IsNull && grup.First() != info)
                        info.PreviousDialog = grup[grup.IndexOf(info) - 1].ToNullableLink();
                    if (info.FormKey.Equals(FormKey.Factory("027F63:Skyrim.esm")))
                        info.Conditions.RemoveAt(0);
                    if (info.FormKey.Equals(FormKey.Factory("02B8BD:Skyrim.esm")))
                    {
                        var conditionData = (GetRelationshipRankConditionData)info.Conditions.First().Data;
                        conditionData.TargetNpc.Link.SetTo(Skyrim.PlacedNpc.FalkFirebeardREF);
                    }
                    if (info.FormKey.Equals(FormKey.Factory("0E7752:Skyrim.esm")) || info.FormKey.Equals(FormKey.Factory("04DDAA:Skyrim.esm")))
                        info.Prompt = null;
                }
            }
            foreach (var recordGetter in records)
            {
                var record = patchMod.DialogTopics.GetOrAddAsOverride(recordGetter);
                var grup = record.Responses;
                foreach (var info in recordGetter.Responses)
                    if (grup.Contains(info.DeepCopy()))
                        grup.Remove(info.DeepCopy());
            }
        }
    }
}