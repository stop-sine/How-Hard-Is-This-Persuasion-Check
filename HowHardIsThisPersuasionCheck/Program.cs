using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using DynamicData;
using static Mutagen.Bethesda.Skyrim.Condition;
using Noggog;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using System.Data;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using CommandLine;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Microsoft.VisualBasic.FileIO;

namespace HowHardIsThisPersuasionCheck
{
    public class Program
    {

        private static readonly Dictionary<IFormLink<IGlobalGetter>, string> SpeechValues = new()
        {
            {Skyrim.Global.SpeechVeryEasy, "Novice"},
            {Skyrim.Global.SpeechEasy, "Apprentice"},
            {Skyrim.Global.SpeechAverage, "Adept"},
            {Skyrim.Global.SpeechHard, "Expert"},
            {Skyrim.Global.SpeechVeryHard, "Master"}
        };

        private static readonly List<FormLink<IDialogTopicGetter>> BrokenRecords =
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
            FormKey.Factory("014035:Dawnguard.esm").ToLink<IDialogTopicGetter>(),
            //FormKey.Factory("000087D:HearthFires.esm")
        ];

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "HHITPC.esp")
                .Run(args);
        }

        private static string GetSpeechValue(Condition? condition)
        {
            return SpeechValues[condition.Cast<ConditionGlobal>().ComparisonValue];
        }

        private static string GetSpeechValue(DialogResponses? info)
        {
            var condition = info?.Conditions.Find(SpeechFilter);
            return SpeechValues[condition.Cast<ConditionGlobal>().ComparisonValue];
        }

        private static Condition? GetSpeechCondition(DialogResponses info)
        {
            return info.Conditions.ToList().Find(SpeechFilter);
        }

        private static ConditionGlobal ConstructSpeech(IFormLink<IGlobalGetter> speechDifficulty, bool flag = default)
        {
            var speech = new ConditionGlobal
            {
                CompareOperator = CompareOperator.GreaterThanOrEqualTo,
                ComparisonValue = speechDifficulty,
                Data = new GetActorValueConditionData
                {
                    RunOnType = RunOnType.Reference,
                    Reference = Skyrim.PlayerRef,
                    ActorValue = ActorValue.Speech
                },
            };
            if (flag)
            {
                speech.CompareOperator = CompareOperator.LessThan;
            }
            return speech;
        }

        private static ConditionFloat ConstructAmulet(bool flag = default)
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
            if (flag)
                amulet.CompareOperator = CompareOperator.NotEqualTo;
            return amulet;
        }

        private static bool SpeechFilter(IConditionGetter condition)
        {
            return condition is IConditionGlobalGetter { Data: IGetActorValueConditionDataGetter { ActorValue: ActorValue.Speech } }
                || condition is IConditionFloatGetter { Data: IGetActorValueConditionDataGetter { ActorValue: ActorValue.Speech } };
        }

        private static bool SpeechFilter(IDialogResponsesGetter subrecord)
            => subrecord.Conditions.Any(SpeechFilter);

        private static bool TextFilter(object record)
        {
            return record switch
            {
                IDialogResponsesGetter r => r.Prompt?.String?.Contains("(Persuade)") ?? false,
                IDialogTopicGetter t => t.Name?.String?.Contains("(Persuade)") ?? false,
                _ => false
            };
        }

        private static bool AmuletFilter(IConditionGetter condition)
        {
            return condition is IConditionFloatGetter { Data: IGetEquippedConditionDataGetter { ItemOrList.Link: var link } } && link.Equals(Skyrim.FormList.TGAmuletofArticulationList);
        }

        private static bool AmuletFilter(IDialogResponsesGetter subrecord)
            => subrecord.Conditions.Any(AmuletFilter);

        private static bool DifferentSpeechChecksFilter(DialogTopic record)
        {
            var values = record.Responses.Where(SpeechFilter)?.Select(GetSpeechCondition).Select(GetSpeechValue);
            return values?.Distinct().Count() > 1;
        }

        private static bool RecordFilter(IDialogTopicGetter record)
        {
            return (TextFilter(record)
                || record.Responses.Any(TextFilter)
                || record.Responses.Any(SpeechFilter)
                || BrokenRecords.Contains(record.ToLink()))
                && record.FormKey != FormKey.Factory("02BDDD:Skyrim.esm");
        }

        private static List<IDialogResponsesGetter> CollectResponses(IEnumerable<IDialogTopicGetter> dialCollection)
        {
            return [.. dialCollection
                .Reverse()
                .SelectMany(dial => dial.Responses)
                .GroupBy(info => info.FormKey)
                .Select(g => g.Last())];
        }

        private static Condition PatchSpeechCondition(Condition condition)
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
            if (condition.CompareOperator == CompareOperator.GreaterThanOrEqualTo)
                condition.Flags = Flag.OR;
            return condition;
        }

        private static Mutagen.Bethesda.Strings.TranslatedString? PatchText(Mutagen.Bethesda.Strings.TranslatedString? text, string speechDifficulty)
        {
            var str = text?.String ?? "";
            if (str.Contains("(Persuade)", StringComparison.OrdinalIgnoreCase))
                return str.Replace("(Persuade)", $"(Persuade: {speechDifficulty})", StringComparison.OrdinalIgnoreCase);
            else if (str.Contains("(Coerce)", StringComparison.OrdinalIgnoreCase))
                return str.Replace("(Coerce)", $"(Coerce: {speechDifficulty})", StringComparison.OrdinalIgnoreCase);
            else if (!str.Contains("(Persuade:", StringComparison.OrdinalIgnoreCase)
                && !str.Contains("(Intimidate)", StringComparison.OrdinalIgnoreCase)
                && !(str.Contains("gold)", StringComparison.OrdinalIgnoreCase) || str.Contains("septim)", StringComparison.OrdinalIgnoreCase)))
                return str + $" (Persuade: {speechDifficulty})";
            return text;
        }

        private static Mutagen.Bethesda.Strings.TranslatedString PatchText(Mutagen.Bethesda.Strings.TranslatedString text)
        {
            var str = text.String ?? "";
            if (str.Contains("(Persuade)", StringComparison.OrdinalIgnoreCase))
                return str.Replace("(Persuade)", "", StringComparison.OrdinalIgnoreCase);
            return text;
        }

        private static bool MatchByConditions(DialogResponses a, DialogResponses b)
        {
            var filter = new Func<IConditionGetter, bool>(c => !(SpeechFilter(c) || AmuletFilter(c)));
            var aData = a.Conditions.Where(filter).Select(c => c.Data);
            var bData = b.Conditions.Where(filter).Select(c => c.Data);
            return aData.SequenceEqual(bData);
        }

        private static void PatchPrompts(ExtendedList<DialogResponses> grup)
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

        private static void SortConditions(ExtendedList<Condition> conditions)
        {
            var speech = conditions.Find(SpeechFilter);
            var amulet = conditions.Find(AmuletFilter);
            if (speech != null) { conditions.Remove(speech); conditions.Add(speech); }
            if (amulet != null) { conditions.Remove(amulet); conditions.Add(amulet); }
        }

        private static void AddFavorGenericScriptProperty(DialogResponses? response, string propertyName = "pFDS")
        {
            response?.VirtualMachineAdapter?.Scripts[0].Properties.Add(new ScriptObjectProperty
            {
                Name = propertyName,
                Flags = ScriptProperty.Flag.Edited,
                Object = Skyrim.Quest.DialogueFavorGeneric
            });
        }

        private static void AddSpeechCondition(DialogResponses response, IFormLink<IGlobalGetter> difficulty)
        {
            response.Conditions.Add(ConstructSpeech(difficulty));
        }

        private static void EnsureOnBeginScriptFragment(DialogResponses? response, string scriptName, string fragmentName)
        {
            if (response?.VirtualMachineAdapter?.ScriptFragments?.OnBegin is null)
                response?.VirtualMachineAdapter?.ScriptFragments?.OnBegin = new ScriptFragment
                {
                    ScriptName = scriptName,
                    FragmentName = fragmentName
                };
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
                Console.WriteLine(record.FormKey);
                var subrecordsGetter = subrecords[record.FormKey];
                var dial = patchMod.DialogTopics.GetOrAddAsOverride(record);
                dial.Responses.Clear();
                dial.Responses.Add(subrecordsGetter.Select(r => r.DeepCopy()));
                var grup = dial.Responses;

                if (dial.Equals(Skyrim.DialogTopic.MG04MirabelleAugurInfoBranchTopic))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("04FA11:Skyrim.esm"));
                    if (baseResponse is not null)
                    {
                        grup.Insert(grup.IndexOf(baseResponse) + 1, new DialogResponses(patchMod)
                        {
                            Conditions = [baseResponse!.Conditions[0], baseResponse.Conditions[1]],
                            Flags = new DialogResponseFlags() { Flags = DialogResponses.Flag.Goodbye },
                            ResponseData = FormKey.Factory("0E0CC4:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>()
                        });
                    }
                }
                if (dial.Equals(Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed3))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0556FA:Skyrim.esm"));
                    if (baseResponse is not null)
                    {
                        dial.Name = baseResponse.Prompt;
                        baseResponse.Prompt = null;
                        AddSpeechCondition(baseResponse, Skyrim.Global.SpeechEasy);
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
                }
                if (dial.Equals(Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed2))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0556F8:Skyrim.esm"));
                    if (baseResponse is not null)
                    {
                        dial.Name = baseResponse.Prompt;
                        baseResponse.Prompt = null;
                        AddSpeechCondition(baseResponse, Skyrim.Global.SpeechAverage);
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
                }
                if (dial.Equals(Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed1))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0556F7:Skyrim.esm"));
                    if (baseResponse is not null)
                    {
                        dial.Name = baseResponse.Prompt;
                        baseResponse.Prompt = null;
                        AddSpeechCondition(baseResponse, Skyrim.Global.SpeechEasy);
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
                }
                if (dial.Equals(Skyrim.DialogTopic.MQ201PartyOndolomarDistractionYes))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("067EC6:Skyrim.esm"));
                    AddFavorGenericScriptProperty(baseResponse);
                    EnsureOnBeginScriptFragment(baseResponse, "TIF__00067EC6", "Fragment_1");
                }
                if (dial.Equals(Skyrim.DialogTopic.DA11IntroVerulusPersuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("060652:Skyrim.esm"));
                    if (baseResponse is not null)
                    {
                        AddSpeechCondition(baseResponse, Skyrim.Global.SpeechEasy);
                        grup.Insert(grup.IndexOf(baseResponse) + 1, new DialogResponses(patchMod)
                        {
                            Conditions = [baseResponse.Conditions[0]],
                            Flags = new DialogResponseFlags() { Flags = DialogResponses.Flag.Goodbye },
                            ResponseData = FormKey.Factory("0E0CC4:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>()
                        });
                    }
                }
                if (dial.Equals(Skyrim.DialogTopic.DB01MiscLoreiusHelpCiceroResponseb))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("07DE91:Skyrim.esm"));
                    if (baseResponse is not null)
                    {
                        dial.Name = baseResponse.Prompt;
                        baseResponse.Prompt = null;
                        AddSpeechCondition(baseResponse, Skyrim.Global.SpeechEasy);
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
                }
                if (dial.Equals(Skyrim.DialogTopic.DB02Captive3Persuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("09DEA6:Skyrim.esm"));
                    if (baseResponse is not null)
                    {
                        dial.Name = baseResponse!.Prompt;
                        baseResponse.Prompt = null;
                        AddSpeechCondition(baseResponse, Skyrim.Global.SpeechAverage);
                        grup.Insert(grup.IndexOf(baseResponse) + 1, new DialogResponses(patchMod)
                        {
                            Conditions = [baseResponse.Conditions[0]],
                            ResponseData = FormKey.Factory("0E0CC5:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                            LinkTo = [Skyrim.DialogTopic.DB02Captive3Intimidate, Skyrim.DialogTopic.DB02Captive3Persuade]
                        });
                    }
                }
                if (dial.Equals(Skyrim.DialogTopic.DB02Captive2Persuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("09DEA1:Skyrim.esm"));
                    if (baseResponse is not null)
                    {
                        dial.Name = baseResponse!.Prompt;
                        baseResponse.Prompt = null;
                        AddSpeechCondition(baseResponse, Skyrim.Global.SpeechAverage);
                        grup.Insert(grup.IndexOf(baseResponse) + 1, new DialogResponses(patchMod)
                        {
                            Conditions = [baseResponse.Conditions[0]],
                            ResponseData = FormKey.Factory("0E0CC4:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                            LinkTo = [Skyrim.DialogTopic.DB02Captive2Intimidate, Skyrim.DialogTopic.DB02Captive2Persuade]
                        });
                    }

                }
                if (dial.Equals(Skyrim.DialogTopic.DB02Captive1Persuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("09DEA2:Skyrim.esm"));
                    if (baseResponse is not null)
                    {
                        dial.Name = baseResponse!.Prompt;
                        baseResponse.Prompt = null;
                        AddSpeechCondition(baseResponse, Skyrim.Global.SpeechAverage);
                        grup.Insert(grup.IndexOf(baseResponse) + 1, new DialogResponses(patchMod)
                        {
                            Conditions = [baseResponse.Conditions[0]],
                            ResponseData = FormKey.Factory("0E0CC3:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                            LinkTo = [Skyrim.DialogTopic.DB02Captive1Intimidate, Skyrim.DialogTopic.DB02Captive1Persuade]
                        });
                    }
                }
                if (dial.Equals(Skyrim.DialogTopic.WERJ02Persuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0B815A:Skyrim.esm"));
                    baseResponse?.Flags?.Flags = DialogResponses.Flag.Goodbye;
                }
                if (dial.Equals(Skyrim.DialogTopic.MQ201PartyDistractionPersuadeSiddgeir))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0C0809:Skyrim.esm"))!;
                    AddFavorGenericScriptProperty(baseResponse);
                    EnsureOnBeginScriptFragment(baseResponse, "TIF__000C0809", "Fragment_2");
                }
                if (dial.Equals(Skyrim.DialogTopic.MQ201PartyDistractionPersuadeIgmund))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0C080D:Skyrim.esm"))!;
                    AddFavorGenericScriptProperty(baseResponse);
                    EnsureOnBeginScriptFragment(baseResponse, "TIF__000C080D", "Fragment_2");
                }
                if (dial.Equals(Skyrim.DialogTopic.MQ201PartyDistractionPersuadeVittoria))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0665D9:Skyrim.esm"))!;
                    AddFavorGenericScriptProperty(baseResponse);
                    EnsureOnBeginScriptFragment(baseResponse, "TIF__000665D9", "Fragment_2");
                }
                if (dial.Equals(Skyrim.DialogTopic.MQ201PartyDistractionPersuadeElisif))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0C0813:Skyrim.esm"))!;
                    AddFavorGenericScriptProperty(baseResponse);
                    EnsureOnBeginScriptFragment(baseResponse, "TIF__000C0813", "Fragment_2");
                }
                if (dial.Equals(Skyrim.DialogTopic.DA14AskAboutStaffPersuadeTopic))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0C4206:Skyrim.esm"))!;
                    AddFavorGenericScriptProperty(baseResponse);

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
                    if (baseResponse is not null)
                    {
                        baseResponse.Conditions.Add(new ConditionFloat
                        {
                            Data = new GetBribeSuccessConditionData { RunOnType = RunOnType.Subject },
                            CompareOperator = CompareOperator.EqualTo,
                            ComparisonValue = 1
                        });
                        if (baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnEnd is not null)
                            baseResponse.VirtualMachineAdapter.ScriptFragments.OnEnd = null;
                        EnsureOnBeginScriptFragment(baseResponse, "TIF__000D197B", "Fragment_2");
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
                }
                if (dial.Equals(Skyrim.DialogTopic.DialogueWhiterunGuardGateStopPersuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0D1981:Skyrim.esm"));
                    if (baseResponse is not null)
                    {
                        AddSpeechCondition(baseResponse, Skyrim.Global.SpeechAverage);
                        baseResponse.Flags!.Flags |= DialogResponses.Flag.SayOnce;
                        if (baseResponse.VirtualMachineAdapter?.ScriptFragments?.OnEnd is not null)
                            baseResponse.VirtualMachineAdapter.ScriptFragments.OnEnd = null;
                        EnsureOnBeginScriptFragment(baseResponse, "TIF__000D1981", "Fragment_1");
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
                }
                if (dial.Equals(Skyrim.DialogTopic.FFRiften22SapphireBranchTopic01))
                {
                    var baseResponse = grup.Find(i => i.FormKey == FormKey.Factory("0D4FC2:Skyrim.esm"));
                    if (baseResponse is not null)
                    {
                        AddSpeechCondition(baseResponse, Skyrim.Global.SpeechAverage);
                        baseResponse.Flags?.Flags = DialogResponses.Flag.Goodbye;
                        baseResponse.Flags?.Flags |= DialogResponses.Flag.SayOnce;
                    }

                    var otherResponse = grup.Find(i => i.FormKey == FormKey.Factory("0D4FC3:Skyrim.esm"));
                    otherResponse?.LinkTo.Clear();
                    otherResponse?.Flags?.Flags = DialogResponses.Flag.Goodbye;
                    otherResponse?.Flags?.Flags |= DialogResponses.Flag.SayOnce;
                }
                if (dial.Equals(Skyrim.DialogTopic.DA03StartLodBranchPersuadeTopic))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0D7933:Skyrim.esm"));
                    if (baseResponse is not null)
                    {
                        AddSpeechCondition(baseResponse, Skyrim.Global.SpeechEasy);
                        AddFavorGenericScriptProperty(baseResponse);
                        EnsureOnBeginScriptFragment(baseResponse, "TIF__000D7933", "Fragment_1");
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
                }
                if (dial.Equals(Skyrim.DialogTopic.DialogueRiftenGateNonNorthBranchTopic02))
                    dial.Name = dial.Name?.String?.Replace("(Persuade)", "");
                if (dial.Equals(Skyrim.DialogTopic.FreeformCidhnaMineADuachPersuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0DB837:Skyrim.esm"));
                    EnsureOnBeginScriptFragment(baseResponse, "TIF__000DB837", "Fragment_1");
                }
                if (dial.Equals(Skyrim.DialogTopic.WE31Persuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("0FF125:Skyrim.esm"))!;
                    baseResponse?.VirtualMachineAdapter!.Scripts[0].Properties.Add(new ScriptObjectProperty
                    {
                        Name = "WEPersuade",
                        Flags = ScriptProperty.Flag.Edited,
                        Object = Skyrim.Quest.DialogueFavorGeneric
                    });
                }
                if (dial.Equals(Skyrim.DialogTopic.WEJS27Persuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("105D0B:Skyrim.esm"))!;
                    baseResponse?.VirtualMachineAdapter!.Scripts[0].Properties.Add(new ScriptObjectProperty
                    {
                        Name = "WEPersuade",
                        Flags = ScriptProperty.Flag.Edited,
                        Object = Skyrim.Quest.DialogueFavorGeneric
                    });
                }
                if (dial.Equals(Skyrim.DialogTopic.WERoad06Persuade))
                {
                    var baseResponse = grup.Find(r => r.FormKey == FormKey.Factory("106015:Skyrim.esm"))!;
                    baseResponse?.VirtualMachineAdapter!.Scripts[0].Properties.Add(new ScriptObjectProperty
                    {
                        Name = "WEPersuade",
                        Flags = ScriptProperty.Flag.Edited,
                        Object = Skyrim.Quest.DialogueFavorGeneric
                    });
                }
                if (dial.Equals(FormKey.Factory("014035:Dawnguard.esm").ToLink<IDialogTopicGetter>()))
                {
                    var response = grup.Find(i => i.FormKey == FormKey.Factory("01403A:Dawnguard.esm"));
                    response?.Clear();
                }
                if (dial.Equals(FormKey.Factory("027573:Dragonborn.esm").ToLink<IDialogTopicGetter>()))
                {
                    var response = grup.Find(i => i.FormKey == FormKey.Factory("0275A4:Dragonborn.esm"));
                    if (response?.Responses != null && response.Responses.Count > 0 && response.Responses[0]?.Text != null)
                    {
                        var originalText = response.Responses[0].Text.String;
                        if (originalText != null)
                            response.Responses[0].Text = originalText.Replace("(Failed)", "");
                    }
                }
                if (dial.Equals(FormKey.Factory("02C07D:Dragonborn.esm").ToLink<IDialogTopicGetter>()))
                {
                    grup.Add(new DialogResponses(patchMod)
                    {
                        Flags = new DialogResponseFlags(),
                        LinkTo = [FormKey.Factory("02C07C:Dragonborn.esm").ToLink<IDialogTopicGetter>(),
                        FormKey.Factory("02C07A:Dragonborn.esm").ToLink<IDialogTopicGetter>(),
                        FormKey.Factory("02C079:Dragonborn.esm").ToLink<IDialogTopicGetter>(),
                        FormKey.Factory("02C078:Dragonborn.esm").ToLink<IDialogTopicGetter>()],
                        ResponseData = FormKey.Factory("0E0CC4:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        Conditions = [new ConditionFloat {
                            CompareOperator = CompareOperator.EqualTo,
                            ComparisonValue = 0,
                            Data = new IsTrespassingConditionData {
                                Reference = Skyrim.PlayerRef
                            }
                        },
                        new ConditionFloat {
                            CompareOperator = CompareOperator.GreaterThan,
                            ComparisonValue = 0,
                            Data = new GetCrimeGoldConditionData {
                                RunOnType = RunOnType.Subject
                            }
                        },
                        new ConditionFloat {
                            CompareOperator = CompareOperator.EqualTo,
                            ComparisonValue = 0,
                            Data = new GetCrimeGoldViolentConditionData {
                                RunOnType = RunOnType.Subject
                            }
                        },
                        new ConditionFloat {
                            CompareOperator = CompareOperator.EqualTo,
                            ComparisonValue = 0,
                            Data = new IsBribedbyPlayerConditionData {
                                RunOnType = RunOnType.Subject
                            }
                        },
                        new ConditionFloat {
                            CompareOperator = CompareOperator.EqualTo,
                            ComparisonValue = 0,
                            Data = new GetIsVoiceTypeConditionData {
                                RunOnType = RunOnType.Subject
                            }
                        }]
                    });
                    grup.Add(new DialogResponses(patchMod)
                    {
                        Flags = new DialogResponseFlags(),
                        LinkTo = [FormKey.Factory("02C07C:Dragonborn.esm").ToLink<IDialogTopicGetter>(),
                        FormKey.Factory("02C07A:Dragonborn.esm").ToLink<IDialogTopicGetter>(),
                        FormKey.Factory("02C079:Dragonborn.esm").ToLink<IDialogTopicGetter>(),
                        FormKey.Factory("02C078:Dragonborn.esm").ToLink<IDialogTopicGetter>()],
                        Responses = [new DialogResponse {
                            Emotion = Emotion.Anger,
                            EmotionValue = 50,
                            ResponseNumber = 1,
                            Flags = DialogResponse.Flag.UseEmotionAnimation,
                            Text = "That's not going to happen.",
                        }],
                        Conditions = [new ConditionFloat {
                            CompareOperator = CompareOperator.EqualTo,
                            ComparisonValue = 0,
                            Data = new IsTrespassingConditionData {
                                Reference = Skyrim.PlayerRef
                            }
                        },
                        new ConditionFloat {
                            CompareOperator = CompareOperator.GreaterThan,
                            ComparisonValue = 0,
                            Data = new GetCrimeGoldConditionData {
                                RunOnType = RunOnType.Subject
                            }
                        },
                        new ConditionFloat {
                            CompareOperator = CompareOperator.EqualTo,
                            ComparisonValue = 0,
                            Data = new GetCrimeGoldViolentConditionData {
                                RunOnType = RunOnType.Subject
                            }
                        },
                        new ConditionFloat {
                            CompareOperator = CompareOperator.EqualTo,
                            ComparisonValue = 0,
                            Data = new IsBribedbyPlayerConditionData {
                                RunOnType = RunOnType.Subject
                            }
                        },
                        new ConditionFloat {
                            CompareOperator = CompareOperator.EqualTo,
                            ComparisonValue = 1,
                            Data = new GetIsVoiceTypeConditionData {
                                RunOnType = RunOnType.Subject
                            }
                        }]
                    });
                    foreach (var info in grup.Where(i => !SpeechFilter(i)))
                        info.Conditions.Last().Data.Cast<GetIsVoiceTypeConditionData>().VoiceTypeOrList.Link.FormKey = FormKey.Factory("018469:Dragonborn.esm");
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

                if (dial.Name is null && grup.Where(SpeechFilter).Count() == 1)
                {
                    var info = grup.Find(i => TextFilter(i) && SpeechFilter(i));
                    dial.Name = PatchText(info?.Prompt, GetSpeechValue(info));
                    info?.Prompt?.Clear();
                }

                if (TextFilter(dial) && !grup.Any(SpeechFilter))
                    dial.Name = PatchText(dial.Name!);

                if (!TextFilter(dial))
                    PatchPrompts(grup);

                if (DifferentSpeechChecksFilter(dial))
                {
                    foreach (var info in grup.Where(SpeechFilter))
                    {
                        var speechDifficulty = GetSpeechValue(GetSpeechCondition(info)!);
                        var matchingInfo = grup.Where(i => i != info).ToList().Find(i => MatchByConditions(info, i));
                        if (info.Prompt?.String is null && dial.Name?.String is not null)
                        {
                            info.Prompt = PatchText(dial.Name, speechDifficulty);
                            matchingInfo?.Prompt = PatchText(dial.Name, speechDifficulty);
                        }
                        else if (info.Prompt?.String is not null)
                        {
                            info.Prompt = PatchText(info.Prompt, speechDifficulty);
                            if (matchingInfo?.Prompt is null)
                                matchingInfo?.Prompt = PatchText(info.Prompt, speechDifficulty);
                            else
                                matchingInfo?.Prompt = PatchText(matchingInfo.Prompt, speechDifficulty);
                        }
                    }
                }

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