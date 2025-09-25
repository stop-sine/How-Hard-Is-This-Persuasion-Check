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
using System.Text.RegularExpressions;

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

        public static string GetSpeechValue(DialogResponses info)
        {
            var condition = info.Conditions.Find(SpeechFilter);
            return SpeechValues[condition.Cast<ConditionGlobal>().ComparisonValue];
        }

        public static IConditionGetter? GetSpeechCondition(IDialogResponsesGetter info)
        {
            return info.Conditions.ToList().Find(SpeechFilter);
        }

        public static Condition? GetSpeechCondition(DialogResponses info)
        {
            return info.Conditions.ToList().Find(SpeechFilter);
        }

        public static ConditionGlobal ConstructSpeech(IFormLink<IGlobalGetter> speechDifficulty)
        {
            var speech = new ConditionGlobal
            {
                Data = new GetActorValueConditionData { RunOnType = RunOnType.Reference, Reference = Skyrim.PlayerRef, ActorValue = ActorValue.Speech },
                CompareOperator = CompareOperator.GreaterThanOrEqualTo,
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
                ComparisonValue = 1,
                Flags = Flag.OR
            };
            if (anti)
                amulet.CompareOperator = CompareOperator.NotEqualTo;
            return amulet;
        }

        public static bool SpeechFilter(IConditionGetter condition)
        {
            return condition is IConditionGlobalGetter { Data: IGetActorValueConditionDataGetter { ActorValue: ActorValue.Speech } }
            || condition is IConditionFloatGetter { Data: IGetActorValueConditionDataGetter { ActorValue: ActorValue.Speech } };
        }

        public static bool SpeechFilter(IDialogResponsesGetter subrecord)
        {
            return subrecord.Conditions.Any(SpeechFilter);
        }

        public static bool TextFilter(IDialogResponsesGetter subrecord)
        {
            return subrecord.Prompt?.String?.Contains("(Persuade)") ?? false;
        }

        public static bool TextFilter(IDialogTopicGetter record)
        {
            return record.Name?.String?.Contains("(Persuade)") ?? false;
        }

        public static bool AmuletFilter(IConditionGetter condition)
        {
            return condition is IConditionFloatGetter { Data: IGetEquippedConditionDataGetter { ItemOrList.Link: var link } } && link.Equals(Skyrim.FormList.TGAmuletofArticulationList);
        }

        public static bool AmuletFilter(IDialogResponsesGetter subrecord)
        {
            return subrecord.Conditions.Any(AmuletFilter);
        }

        public static bool DifferentSpeechChecksFilter(DialogTopic record)
        {
            var conditions = record.Responses.Where(SpeechFilter).Select(GetSpeechCondition);
            var values = conditions.Select(GetSpeechValue!);
            return values.Distinct().Count() > 1;
        }

        public static bool RecordFilter(IDialogTopicGetter record)
        {
            return (TextFilter(record)
            || record.Responses.Any(TextFilter)
            || record.Responses.Any(SpeechFilter)
            || BrokenRecords.Contains(record.ToLink()))
            && record.FormKey != FormKey.Factory("02BDDD:Skyrim.esm");
        }

        public static List<IDialogResponsesGetter> CollectResponses(IEnumerable<IDialogTopicGetter> dialCollection)
        {
            var infoCollection = new Dictionary<FormKey, IDialogResponsesGetter>();
            foreach (var dial in dialCollection.Reverse())
                foreach (var info in dial.Responses)
                    if (!infoCollection.TryAdd(info.FormKey, info))
                        infoCollection[info.FormKey] = info;
            return [.. infoCollection.Values];
        }

        public static Condition PatchSpeechCondition(Condition condition)
        {
            if (condition is ConditionFloat floatCondition)
            {
                ConditionGlobal? globalCondition = floatCondition.ComparisonValue switch
                {
                    10 => ConstructSpeech(Skyrim.Global.SpeechVeryEasy),
                    25 => ConstructSpeech(Skyrim.Global.SpeechEasy),
                    50 => ConstructSpeech(Skyrim.Global.SpeechAverage),
                    75 => ConstructSpeech(Skyrim.Global.SpeechHard),
                    100 => ConstructSpeech(Skyrim.Global.SpeechVeryHard),
                    _ => null
                };
                if (globalCondition is not null)
                    condition = globalCondition;
            }
            condition.Data.RunOnType = RunOnType.Reference;
            condition.Data.Reference = Skyrim.PlayerRef;
            condition.Flags = Flag.OR;
            return condition;
        }

        public static Mutagen.Bethesda.Strings.TranslatedString PatchText(Mutagen.Bethesda.Strings.TranslatedString text, string speechDifficulty)
        {
            if (text.String!.Contains("(Persuade)", StringComparison.OrdinalIgnoreCase))
                text = text.String.Replace("(Persuade)", $"(Persuade: {speechDifficulty})", StringComparison.OrdinalIgnoreCase);
            else if (!text.String!.Contains("(Intimidate)", StringComparison.OrdinalIgnoreCase) &&
                 !(text.String!.Contains("gold)", StringComparison.OrdinalIgnoreCase) ||
                   text.String!.Contains("septim)", StringComparison.OrdinalIgnoreCase)))
                text += $" (Persuade: {speechDifficulty})";
            return text;
        }

        public static Mutagen.Bethesda.Strings.TranslatedString PatchText(Mutagen.Bethesda.Strings.TranslatedString text)
        {
            if (text.String!.Contains("(Persuade)", StringComparison.OrdinalIgnoreCase))
                text = text.String.Replace("(Persuade)", $"", StringComparison.OrdinalIgnoreCase);
            return text;
        }

        public static bool MatchByConditions(DialogResponses a, DialogResponses b)
        {
            var conditionsA = a.Conditions.Where(c => !(SpeechFilter(c) || AmuletFilter(c))).ToList();
            var conditionsB = b.Conditions.Where(c => !(SpeechFilter(c) || AmuletFilter(c))).ToList();
            var conditionsAData = conditionsA.Select(c => c.Data);
            var conditionsBData = conditionsB.Select(c => c.Data);
            return conditionsAData.SequenceEqual(conditionsBData);
        }

        public static void PatchPrompts(ExtendedList<DialogResponses> grup)
        {
            var infoCollection = grup.Where(r => r.Prompt?.String is { Length: > 0 } && SpeechFilter(r));
            foreach (var info in infoCollection)
            {
                var promptText = info.Prompt!.String!;
                var matchingInfo = grup.Where(r => r.Prompt?.String == promptText);
                foreach (var match in matchingInfo)
                    match.Prompt = PatchText(match.Prompt!, GetSpeechValue(info));
            }
        }

        public static void SortConditions(ExtendedList<Condition> conditions)
        {
            var speech = conditions.Find(SpeechFilter)!;
            var amulet = conditions.Find(AmuletFilter)!;
            conditions.Remove(speech);
            conditions.Remove(amulet);
            conditions.Add(speech);
            conditions.Add(amulet);
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var cache = state.LinkCache;
            var patchMod = state.PatchMod;
            var records = state.LoadOrder.PriorityOrder.DialogTopic().WinningOverrides().Where(RecordFilter).ToList();
            var subrecords = records.ToDictionary(
                d => d.FormKey,
                d => CollectResponses(d.FormKey.ToLinkGetter<IDialogTopicGetter>().ResolveAll(cache))
            );

            Console.WriteLine($"Found {records.Count} records to be patched");

            foreach (var record in records)
            {
                var subrecordsGetter = subrecords[record.FormKey];
                var dial = patchMod.DialogTopics.GetOrAddAsOverride(record);
                dial.Responses.Clear();
                dial.Responses.Add(subrecordsGetter.Select(r => r.DeepCopy()));
                var grup = dial.Responses;

                if (dial.Equals(Skyrim.DialogTopic.MG04MirabelleAugurInfoBranchTopic))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("04FA11:Skyrim.esm"));
                    grup.Insert(grup.IndexOf(baseResponse!) + 1, new DialogResponses(patchMod)
                    {
                        Conditions = [baseResponse!.Conditions[0], baseResponse.Conditions[1]],
                        Flags = new DialogResponseFlags() { Flags = DialogResponses.Flag.Goodbye },
                        ResponseData = FormKey.Factory("0E0CC4:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>()
                    });
                }
                if (dial.Equals(Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed3))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0556FA:Skyrim.esm"));
                    dial.Name = baseResponse!.Prompt;
                    baseResponse.Prompt = null;
                    baseResponse.Conditions.Add(ConstructSpeech(Skyrim.Global.SpeechEasy));
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
                if (dial.Equals(Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed2))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0556F8:Skyrim.esm"));
                    dial.Name = baseResponse!.Prompt;
                    baseResponse.Prompt = null;
                    baseResponse.Conditions.Add(ConstructSpeech(Skyrim.Global.SpeechAverage));
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
                if (dial.Equals(Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed1))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0556F7:Skyrim.esm"));
                    dial.Name = baseResponse!.Prompt;
                    baseResponse.Prompt = null;
                    baseResponse.Conditions.Add(ConstructSpeech(Skyrim.Global.SpeechEasy));
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
                if (dial.Equals(Skyrim.DialogTopic.MQ201PartyOndolomarDistractionYes))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("067EC6:Skyrim.esm"))!;
                    baseResponse.VirtualMachineAdapter!.Scripts[0].Properties.Add(new ScriptObjectProperty
                    {
                        Name = "pFDS",
                        Flags = ScriptProperty.Flag.Edited,
                        Object = Skyrim.Quest.DialogueFavorGeneric
                    });
                    if (baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnBegin is null)
                        baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnBegin = new ScriptFragment
                        {
                            ScriptName = "TIF__00067EC6",
                            FragmentName = "Fragment_1"
                        };
                }
                if (dial.Equals(Skyrim.DialogTopic.DA11IntroVerulusPersuade))
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
                if (dial.Equals(Skyrim.DialogTopic.DB01MiscLoreiusHelpCiceroResponseb))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("07DE91:Skyrim.esm"));
                    dial.Name = baseResponse!.Prompt;
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
                if (dial.Equals(Skyrim.DialogTopic.DB02Captive3Persuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("09DEA6:Skyrim.esm"));
                    dial.Name = baseResponse!.Prompt;
                    baseResponse.Prompt = null;
                    baseResponse.Conditions.Add(ConstructSpeech(Skyrim.Global.SpeechAverage));
                    grup.Insert(grup.IndexOf(baseResponse) + 1, new DialogResponses(patchMod)
                    {
                        Conditions = [baseResponse.Conditions[0]],
                        ResponseData = FormKey.Factory("0E0CC5:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        LinkTo = [Skyrim.DialogTopic.DB02Captive3Intimidate, Skyrim.DialogTopic.DB02Captive3Persuade]
                    });
                }
                if (dial.Equals(Skyrim.DialogTopic.DB02Captive2Persuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("09DEA1:Skyrim.esm"));
                    dial.Name = baseResponse!.Prompt;
                    baseResponse.Prompt = null;
                    baseResponse.Conditions.Add(ConstructSpeech(Skyrim.Global.SpeechAverage));
                    grup.Insert(grup.IndexOf(baseResponse) + 1, new DialogResponses(patchMod)
                    {
                        Conditions = [baseResponse.Conditions[0]],
                        ResponseData = FormKey.Factory("0E0CC4:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        LinkTo = [Skyrim.DialogTopic.DB02Captive2Intimidate, Skyrim.DialogTopic.DB02Captive2Persuade]
                    });
                }
                if (dial.Equals(Skyrim.DialogTopic.DB02Captive1Persuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("09DEA2:Skyrim.esm"));
                    dial.Name = baseResponse!.Prompt;
                    baseResponse.Prompt = null;
                    baseResponse.Conditions.Add(ConstructSpeech(Skyrim.Global.SpeechAverage));
                    grup.Insert(grup.IndexOf(baseResponse) + 1, new DialogResponses(patchMod)
                    {
                        Conditions = [baseResponse.Conditions[0]],
                        ResponseData = FormKey.Factory("0E0CC3:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        LinkTo = [Skyrim.DialogTopic.DB02Captive1Intimidate, Skyrim.DialogTopic.DB02Captive1Persuade]
                    });
                }
                if (dial.Equals(Skyrim.DialogTopic.WERJ02Persuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0B815A:Skyrim.esm"))!;
                    baseResponse.Flags?.Flags = DialogResponses.Flag.Goodbye;
                }
                if (dial.Equals(Skyrim.DialogTopic.MQ201PartyDistractionPersuadeSiddgeir))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0C0809:Skyrim.esm"))!;
                    baseResponse.VirtualMachineAdapter!.Scripts[0].Properties.Add(new ScriptObjectProperty
                    {
                        Name = "pFDS",
                        Flags = ScriptProperty.Flag.Edited,
                        Object = Skyrim.Quest.DialogueFavorGeneric
                    });
                    if (baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnBegin is null)
                        baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnBegin = new ScriptFragment
                        {
                            ScriptName = "TIF__000C0809",
                            FragmentName = "Fragment_2"
                        };
                }
                if (dial.Equals(Skyrim.DialogTopic.MQ201PartyDistractionPersuadeIgmund))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0C080D:Skyrim.esm"))!;
                    baseResponse.VirtualMachineAdapter!.Scripts[0].Properties.Add(new ScriptObjectProperty
                    {
                        Name = "pFDS",
                        Flags = ScriptProperty.Flag.Edited,
                        Object = Skyrim.Quest.DialogueFavorGeneric
                    });
                    if (baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnBegin is null)
                        baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnBegin = new ScriptFragment
                        {
                            ScriptName = "TIF__000C080D",
                            FragmentName = "Fragment_2"
                        };
                }
                if (dial.Equals(Skyrim.DialogTopic.MQ201PartyDistractionPersuadeVittoria))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0665D9:Skyrim.esm"))!;
                    baseResponse.VirtualMachineAdapter!.Scripts[0].Properties.Add(new ScriptObjectProperty
                    {
                        Name = "pFDS",
                        Flags = ScriptProperty.Flag.Edited,
                        Object = Skyrim.Quest.DialogueFavorGeneric
                    });
                    if (baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnBegin is null)
                        baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnBegin = new ScriptFragment
                        {
                            ScriptName = "TIF__000665D9",
                            FragmentName = "Fragment_2"
                        };
                }
                if (dial.Equals(Skyrim.DialogTopic.MQ201PartyDistractionPersuadeElisif))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0C0813:Skyrim.esm"))!;
                    baseResponse.VirtualMachineAdapter!.Scripts[0].Properties.Add(new ScriptObjectProperty
                    {
                        Name = "pFDS",
                        Flags = ScriptProperty.Flag.Edited,
                        Object = Skyrim.Quest.DialogueFavorGeneric
                    });
                    if (baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnBegin is null)
                        baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnBegin = new ScriptFragment
                        {
                            ScriptName = "TIF__000C0813",
                            FragmentName = "Fragment_2"
                        };
                }
                if (dial.Equals(Skyrim.DialogTopic.DA14AskAboutStaffPersuadeTopic))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0C4206:Skyrim.esm"))!;
                    baseResponse.VirtualMachineAdapter!.Scripts[0].Properties.Add(new ScriptObjectProperty
                    {
                        Name = "pFDS",
                        Flags = ScriptProperty.Flag.Edited,
                        Object = Skyrim.Quest.DialogueFavorGeneric
                    });

                }
                if (dial.Equals(Skyrim.DialogTopic.DialogueWhiterunGuardGateStopIntimidate))
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
                if (dial.Equals(Skyrim.DialogTopic.DialogueWhiterunGuardGateStopBribe))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0D197B:Skyrim.esm"));
                    baseResponse!.Conditions.Add(new ConditionFloat
                    {
                        Data = new GetBribeSuccessConditionData { RunOnType = RunOnType.Subject },
                        CompareOperator = CompareOperator.EqualTo,
                        ComparisonValue = 1
                    });
                    baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnEnd?.Clear();
                    if (baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnBegin is null)
                        baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnBegin = new ScriptFragment
                        {
                            ScriptName = "TIF__000D197B",
                            FragmentName = "Fragment_2"
                        };

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
                if (dial.Equals(Skyrim.DialogTopic.DialogueWhiterunGuardGateStopPersuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0D1981:Skyrim.esm"));
                    baseResponse?.Conditions.Add(ConstructSpeech(Skyrim.Global.SpeechAverage));
                    baseResponse!.Flags!.Flags |= DialogResponses.Flag.SayOnce;
                    baseResponse.VirtualMachineAdapter!.ScriptFragments!.OnEnd?.Clear();
                    if (baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnBegin is null)
                        baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnBegin = new ScriptFragment
                        {
                            ScriptName = "TIF__000D1981",
                            FragmentName = "Fragment_1"
                        };
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
                if (dial.Equals(Skyrim.DialogTopic.DA03StartLodBranchPersuadeTopic))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0D7933:Skyrim.esm"));
                    baseResponse!.Conditions.Add(ConstructSpeech(Skyrim.Global.SpeechEasy));
                    baseResponse.VirtualMachineAdapter!.Scripts[0].Properties.Add(new ScriptObjectProperty
                    {
                        Name = "pFDS",
                        Flags = ScriptProperty.Flag.Edited,
                        Object = Skyrim.Quest.DialogueFavorGeneric
                    });
                    if (baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnBegin is null)
                        baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnBegin = new ScriptFragment
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
                if (dial.Equals(Skyrim.DialogTopic.DialogueRiftenGateNonNorthBranchTopic02))
                    dial.Name = dial.Name?.String?.Replace("(Persuade)", "");
                if (dial.Equals(Skyrim.DialogTopic.FreeformCidhnaMineADuachPersuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0DB837:Skyrim.esm"))!;
                    if (baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnBegin is null)
                        baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnBegin = new ScriptFragment
                        {
                            ScriptName = "TIF__000DB837",
                            FragmentName = "Fragment_1"
                        };
                }
                if (dial.Equals(Skyrim.DialogTopic.WE31Persuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0FF125:Skyrim.esm"))!;
                    baseResponse.VirtualMachineAdapter!.Scripts[0].Properties.Add(new ScriptObjectProperty
                    {
                        Name = "WEPersuade",
                        Flags = ScriptProperty.Flag.Edited,
                        Object = Skyrim.Quest.DialogueFavorGeneric
                    });
                }
                if (dial.Equals(Skyrim.DialogTopic.WEJS27Persuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("105D0B:Skyrim.esm"))!;
                    baseResponse.VirtualMachineAdapter!.Scripts[0].Properties.Add(new ScriptObjectProperty
                    {
                        Name = "WEPersuade",
                        Flags = ScriptProperty.Flag.Edited,
                        Object = Skyrim.Quest.DialogueFavorGeneric
                    });
                }
                if (dial.Equals(Skyrim.DialogTopic.WERoad06Persuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("106015:Skyrim.esm"))!;
                    baseResponse.VirtualMachineAdapter!.Scripts[0].Properties.Add(new ScriptObjectProperty
                    {
                        Name = "WEPersuade",
                        Flags = ScriptProperty.Flag.Edited,
                        Object = Skyrim.Quest.DialogueFavorGeneric
                    });
                }

                foreach (var info in grup.Where(SpeechFilter))
                {
                    var speechIndex = info.Conditions.FindIndex(SpeechFilter)!;
                    info.Conditions[speechIndex] = PatchSpeechCondition(info.Conditions[speechIndex]);
                    if (!AmuletFilter(info))
                        info.Conditions.Insert(speechIndex + 1, ConstructAmulet(info.Conditions[speechIndex].CompareOperator != CompareOperator.GreaterThanOrEqualTo));
                    SortConditions(info.Conditions);
                }

                if (dial.Name?.String is not null && grup.Any(SpeechFilter) && !DifferentSpeechChecksFilter(dial) && !dial.Equals(Skyrim.DialogTopic.MS09ThoraldQuestionsBranchTopic))
                {
                    var speech = grup.First(SpeechFilter).Conditions.First(SpeechFilter);
                    dial.Name = PatchText(dial.Name, GetSpeechValue(speech));
                }

                if (TextFilter(dial) && !grup.Any(SpeechFilter))
                    dial.Name = PatchText(dial.Name!);

                if (DifferentSpeechChecksFilter(dial))
                {
                    Console.WriteLine(dial.FormKey);
                    Console.WriteLine(dial.EditorID);
                    foreach (var info in grup.Where(SpeechFilter))
                    {
                        var speechDifficulty = GetSpeechValue(GetSpeechCondition(info)!);
                        var matchingInfo = grup.Where(i => i != info).ToList().Find(i => MatchByConditions(info, i));
                        if (matchingInfo is not null)
                            if (dial.Name?.String is not null)
                            {
                                info.Prompt = PatchText(dial.Name, speechDifficulty);
                                matchingInfo.Prompt = PatchText(dial.Name, speechDifficulty);
                            }
                            else if (info.Prompt?.String is not null)
                            {
                                info.Prompt = PatchText(info.Prompt, speechDifficulty);
                                matchingInfo.Prompt = PatchText(info.Prompt, speechDifficulty);
                            }
                    }
                }

                if (!TextFilter(dial) && !DifferentSpeechChecksFilter(dial))
                    PatchPrompts(grup);

                foreach (var info in grup)
                {
                    if (info.PreviousDialog.IsNull && grup.IndexOf(info) != 0)
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
            foreach (var record in records)
            {
                var dial = patchMod.DialogTopics.GetOrAddAsOverride(record);
                var subrecordsGetter = subrecords[record.FormKey];
                var grup = dial.Responses;
                foreach (var info in subrecordsGetter)
                    if (grup.Contains(info.DeepCopy()))
                        grup.Remove(info.DeepCopy());
            }
        }
    }
}