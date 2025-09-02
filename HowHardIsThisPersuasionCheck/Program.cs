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

namespace HowHardIsThisPersuasionCheck
{
    public class Program
    {
        public static readonly List<FormKey> SpeechGlobals =
        [
            Skyrim.Global.SpeechVeryEasy.FormKey,
            Skyrim.Global.SpeechEasy.FormKey,
            Skyrim.Global.SpeechAverage.FormKey,
            Skyrim.Global.SpeechHard.FormKey,
            Skyrim.Global.SpeechVeryHard.FormKey
        ];

        public static readonly Dictionary<FormKey, string> SpeechValues = new()
        {
            {SpeechGlobals[0], "Novice"},
            {SpeechGlobals[1], "Apprentice"},
            {SpeechGlobals[2], "Adept"},
            {SpeechGlobals[3], "Expert"},
            {SpeechGlobals[4], "Master"}
        };

        public static readonly List<FormKey> BrokenRecords =
        [
            Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed3.FormKey,
            Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed2.FormKey,
            Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed1.FormKey,
            Skyrim.DialogTopic.DA11IntroVerulusPersuade.FormKey,
            Skyrim.DialogTopic.DB01MiscLoreiusHelpCiceroResponseb.FormKey,
            Skyrim.DialogTopic.DB02Captive3Persuade.FormKey,
            Skyrim.DialogTopic.DB02Captive2Persuade.FormKey,
            Skyrim.DialogTopic.DB02Captive1Persuade.FormKey,
            Skyrim.DialogTopic.DialogueWhiterunGuardGateStopIntimidate.FormKey,
            Skyrim.DialogTopic.DialogueWhiterunGuardGateStopBribe.FormKey,
            Skyrim.DialogTopic.DialogueWhiterunGuardGateStopPersuade.FormKey,
            Skyrim.DialogTopic.DA03StartLodBranchPersuadeTopic.FormKey,
            //FormKey.Factory("000087D:HearthFires.esm")
        ];

        public static readonly List<FormKey> BrokenSubrecords =
        [
            FormKey.Factory("0E7752:Skyrim.esm"),
            FormKey.Factory("04DDAA:Skyrim.esm"),
            FormKey.Factory("02B8BD:Skyrim.esm"),
            FormKey.Factory("060652:Skyrim.esm"),
            FormKey.Factory("07DE90:Skyrim.esm"),
            FormKey.Factory("09080E:Skyrim.esm"),
            FormKey.Factory("09080F:Skyrim.esm"),
            FormKey.Factory("090810:Skyrim.esm"),
            FormKey.Factory("0D197F:Skyrim.esm"),
            FormKey.Factory("0D197B:Skyrim.esm"),
            FormKey.Factory("0D1981:Skyrim.esm"),
            FormKey.Factory("0D7933:Skyrim.esm")
        ];

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "HHITPC.esp")
                .Run(args);
        }

        public static ConditionFloat? GetAmuletCondition(DialogResponses info)
        {
            Condition condition = info.Conditions.FirstOrDefault(c => c.Data is GetEquippedConditionData e && e.ItemOrList.Link.Equals(Skyrim.FormList.TGAmuletofArticulationList))!;
            return condition as ConditionFloat;
        }

        public static string GetSpeechValue(Condition condition)
        {
            return SpeechValues[condition.Cast<ConditionGlobal>().ComparisonValue.FormKey];
        }

        public static IConditionGetter? GetSpeechCondition(IDialogResponsesGetter? info)
        {
            return info?.Conditions.FirstOrDefault(c => c is IConditionGlobalGetter global && SpeechGlobals.Contains(global.ComparisonValue.FormKey));
        }

        public static Condition? GetSpeechCondition(DialogResponses? info)
        {
            return info?.Conditions.FirstOrDefault(c => c is ConditionGlobal global && SpeechGlobals.Contains(global.ComparisonValue.FormKey));
        }

        public static bool GetSpeechCondition(IDialogResponsesGetter info, out IConditionGetter? condition)
        {
            condition = info.Conditions.FirstOrDefault(c => c is IConditionGlobalGetter global && SpeechGlobals.Contains(global.ComparisonValue.FormKey));
            return condition != null;
        }

        public static bool GetSpeechCondition(ExtendedList<Condition> conditions, out Condition? condition)
        {
            condition = conditions.FirstOrDefault(c => c is ConditionGlobal global && SpeechGlobals.Contains(global.ComparisonValue.FormKey));
            return condition != null;
        }

        public static IReadOnlyList<IDialogResponsesGetter>? GetSpeechResponses(IReadOnlyList<IDialogResponsesGetter> grup)
        {
            var responses = grup.Where(info => GetSpeechCondition(info) != null).ToList();
            if (responses.Count > 0)
                return responses;
            else
                return null;
        }

        public static bool GetSpeechResponses(IReadOnlyList<IDialogResponsesGetter> grup, out List<IDialogResponsesGetter> subrecords)
        {
            subrecords = [.. grup.Where(info => GetSpeechCondition(info, out _))];
            return subrecords.Count > 0;
        }

        public static ConditionGlobal ConstructSpeech(FormKey speechDifficulty)
        {
            var speech = new ConditionGlobal
            {
                Data = new GetActorValueConditionData { RunOnType = RunOnType.Reference, Reference = Skyrim.PlayerRef, ActorValue = ActorValue.Speech },
                CompareOperator = CompareOperator.GreaterThan,
                ComparisonValue = speechDifficulty.ToLink<IGlobalGetter>()
            };
            return speech;
        }

        public static ConditionFloat ConstructAmulet()
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
            return amulet;
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var cache = state.LinkCache;
            var allRecords = state.LoadOrder.PriorityOrder.DialogTopic().WinningOverrides();
            var recordGetters = allRecords.Where(r => GetSpeechResponses(r.Responses) is not null || BrokenRecords.Contains(r.FormKey)).ToList();
            var records = recordGetters.Select(r => state.PatchMod.DialogTopics.GetOrAddAsOverride(r)).ToList();
            var subrecords = recordGetters.ToDictionary(r => r.FormKey, r => r.Responses.Select(info => info.DeepCopy()).ToList());
            //Console.WriteLine($"Found {recordGetters.Count} records to be patched");
            //recordGetters.ForEach(record => Console.WriteLine(record.EditorID));
            foreach (var recordGetter in recordGetters)
            {
                GetSpeechResponses(recordGetter.Responses, out var speechResponses);
                foreach (var info in recordGetter.Responses)
                {
                    if (speechResponses.Contains(info)
                        || (info.PreviousDialog.IsNull && recordGetter.Responses.IndexOf(info) != 0)
                        || (info.Prompt is not null && info.Prompt.String is not null && info.Prompt.String.Contains("(Persuade)") == true)
                        || (info.Prompt is null && speechResponses.Count > 1 && !speechResponses.Any(r => GetSpeechCondition(r) != GetSpeechCondition(speechResponses[0])))
                        || BrokenSubrecords.Contains(info.FormKey))
                    {
                        records[recordGetters.IndexOf(recordGetter)].Responses.Add(info.DeepCopy());
                    }
                }
            }
            foreach (var record in records)
            {
                if (record.FormKey.Equals(BrokenRecords[0]) || record.FormKey.Equals(BrokenRecords[1]) || record.FormKey.Equals(BrokenRecords[2]))
                {
                    record.Name = record.Responses[0].Prompt;
                    record.Responses[0].Prompt = null;
                    if (record.FormKey.Equals(BrokenRecords[0]) || record.FormKey.Equals(BrokenRecords[2]))
                        record.Responses[0].Conditions.Add(ConstructSpeech(SpeechGlobals[1]));
                    else
                        record.Responses[0].Conditions.Add(ConstructSpeech(SpeechGlobals[2]));
                    var newResponse = new DialogResponses(state.PatchMod)
                    {
                        ResponseData = FormKey.Factory("0E0CC4:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        Flags = new DialogResponseFlags(),
                        Conditions = [record.Responses[0].Conditions[0], record.Responses[0].Conditions[1]],
                        LinkTo = [Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed1,
                        Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed2,
                        Skyrim.DialogTopic.DB01MiscGuardPlayerCiceroFramed3]
                    };
                    record.Responses.Add(newResponse);
                    subrecords[record.FormKey].Add(newResponse);
                }
                if (record.FormKey.Equals(BrokenRecords[3]))
                {
                    record.Responses[0].Conditions.Add(ConstructSpeech(SpeechGlobals[1]));
                    var newResponse = new DialogResponses(state.PatchMod)
                    {
                        ResponseData = FormKey.Factory("0E0CC4:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        Flags = new DialogResponseFlags()
                    };
                    newResponse.Flags.Flags = DialogResponses.Flag.Goodbye;
                    newResponse.Conditions.Add(record.Responses[0].Conditions[0]);
                    record.Responses.Add(newResponse);
                    subrecords[record.FormKey].Add(newResponse);
                }
                if (record.FormKey.Equals(BrokenRecords[4]))
                {
                    record.Name = record.Responses[0].Prompt;
                    record.Responses[0].Prompt = null;
                    record.Responses[0].Conditions.Add(ConstructSpeech(SpeechGlobals[1]));
                    var newResponse = new DialogResponses(state.PatchMod)
                    {
                        ResponseData = FormKey.Factory("0E0CC4:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        Flags = new DialogResponseFlags(),
                        Conditions = [record.Responses[0].Conditions[0]],
                        LinkTo = [Skyrim.DialogTopic.DB01MiscLoreiusScrewCiceroYes,
                        Skyrim.DialogTopic.DB01MiscLoreiusScrewCiceroNo,
                        Skyrim.DialogTopic.DB01MiscLoreiusHelpCiceroResponseb]
                    };
                    newResponse.Flags.Flags = DialogResponses.Flag.Goodbye;
                    record.Responses.Add(newResponse);
                    subrecords[record.FormKey].Add(newResponse);
                }
                if (record.FormKey.Equals(BrokenRecords[5]) || record.FormKey.Equals(BrokenRecords[6]) || record.FormKey.Equals(BrokenRecords[7]))
                {
                    record.Name = record.Responses[0].Prompt;
                    record.Responses[0].Prompt = null;
                    record.Responses[0].Conditions.Add(ConstructSpeech(SpeechGlobals[2]));
                    var newResponse = new DialogResponses(state.PatchMod)
                    {
                        Flags = new DialogResponseFlags(),
                        Conditions = [record.Responses[0].Conditions[0]],
                    };
                    if (record.FormKey.Equals(BrokenRecords[5]))
                    {
                        newResponse.ResponseData = FormKey.Factory("0E0CC5:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>();
                        newResponse.LinkTo.AddRange([Skyrim.DialogTopic.DB02Captive3Intimidate, Skyrim.DialogTopic.DB02Captive3Persuade]);
                    }
                    if (record.FormKey.Equals(BrokenRecords[6]))
                    {
                        newResponse.ResponseData = FormKey.Factory("0E0CC4:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>();
                        newResponse.LinkTo.AddRange([Skyrim.DialogTopic.DB02Captive2Intimidate, Skyrim.DialogTopic.DB02Captive2Persuade]);
                    }
                    if (record.FormKey.Equals(BrokenRecords[7]))
                    {
                        newResponse.ResponseData = FormKey.Factory("0E0CC3:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>();
                        newResponse.LinkTo.AddRange([Skyrim.DialogTopic.DB02Captive1Intimidate, Skyrim.DialogTopic.DB02Captive1Persuade]);
                    }
                    record.Responses.Add(newResponse);
                    subrecords[record.FormKey].Add(newResponse);
                }
                if (record.FormKey.Equals(BrokenRecords[8]))
                {
                    record.Responses[0].Conditions.Add(new ConditionFloat
                    {
                        Data = new GetIntimidateSuccessConditionData { RunOnType = RunOnType.Subject },
                        CompareOperator = CompareOperator.EqualTo,
                        ComparisonValue = 0
                    });
                    var newResponse = new DialogResponses(state.PatchMod)
                    {
                        VirtualMachineAdapter = record.Responses[0].VirtualMachineAdapter,
                        Flags = new DialogResponseFlags(),
                        ResponseData = FormKey.Factory("0E0CBC:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        Conditions = [record.Responses[0].Conditions[0], new ConditionFloat{
                        Data = new GetIntimidateSuccessConditionData { RunOnType = RunOnType.Subject }, CompareOperator = CompareOperator.EqualTo, ComparisonValue = 1}]
                    };
                    newResponse.Flags.Flags = DialogResponses.Flag.Goodbye;
                    record.Responses[0].VirtualMachineAdapter = null;
                    record.Responses.Add(newResponse);
                    subrecords[record.FormKey].Add(newResponse);
                }
                if (record.FormKey.Equals(BrokenRecords[9]))
                {
                    record.Responses[0].Conditions.Add(new ConditionFloat
                    {
                        Data = new GetBribeSuccessConditionData { RunOnType = RunOnType.Subject },
                        CompareOperator = CompareOperator.EqualTo,
                        ComparisonValue = 1
                    });
                    var newResponse = new DialogResponses(state.PatchMod)
                    {
                        Flags = new DialogResponseFlags(),
                        ResponseData = FormKey.Factory("0E0CC4:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        Conditions = [record.Responses[0].Conditions[0], new ConditionFloat{
                        Data = new GetIntimidateSuccessConditionData { RunOnType = RunOnType.Subject }, CompareOperator = CompareOperator.EqualTo, ComparisonValue = 0}],
                        LinkTo = [Skyrim.DialogTopic.DialogueWhiterunGuardGateStopNote,
                        Skyrim.DialogTopic.DialogueWhiterunGuardGateStopPersuade,
                        Skyrim.DialogTopic.DialogueWhiterunGuardGateStopBribe,
                        Skyrim.DialogTopic.DialogueWhiterunGuardGateStopIntimidate,
                        Skyrim.DialogTopic.DialogueWhiterunGuardGateStopNevermind]
                    };
                    record.Responses.Add(newResponse);
                    subrecords[record.FormKey].Add(newResponse);
                }
                if (record.FormKey.Equals(BrokenRecords[10]))
                {
                    record.Responses[0].Conditions.Add(ConstructSpeech(SpeechGlobals[2]));
                    record.Responses[0].Flags!.Flags |= DialogResponses.Flag.SayOnce;
                    var newResponse = new DialogResponses(state.PatchMod)
                    {
                        Flags = new DialogResponseFlags(),
                        LinkTo = [Skyrim.DialogTopic.DialogueWhiterunGuardGateStopNote,
                        Skyrim.DialogTopic.DialogueWhiterunGuardGateStopPersuade,
                        Skyrim.DialogTopic.DialogueWhiterunGuardGateStopBribe,
                        Skyrim.DialogTopic.DialogueWhiterunGuardGateStopIntimidate,
                        Skyrim.DialogTopic.DialogueWhiterunGuardGateStopNevermind],
                        ResponseData = FormKey.Factory("0E0CC3:Skyrim.esm").ToNullableLink<IDialogResponsesGetter>(),
                        Conditions = [record.Responses[0].Conditions[0]]
                    };
                    record.Responses.Add(newResponse);
                    subrecords[record.FormKey].Add(newResponse);
                }
                if (record.FormKey.Equals(BrokenRecords[11]))
                {
                    //record.Responses[0].Conditions.RemoveAt(0);
                    record.Responses[0].Conditions.Add(ConstructSpeech(SpeechGlobals[1]));
                    record.Responses[0].VirtualMachineAdapter!.Scripts[0].Properties.Add(new ScriptObjectProperty
                    {
                        Name = "pFDS",
                        Flags = ScriptProperty.Flag.Edited,
                        Object = Skyrim.Quest.DialogueFavorGeneric
                    });
                    record.Responses[0].VirtualMachineAdapter!.ScriptFragments!.OnBegin = new ScriptFragment
                    {
                        ScriptName = "TIF__000D7933",
                        FragmentName = "Fragment_1"
                    };
                    var newResponse = new DialogResponses(state.PatchMod)
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
                    record.Responses.Add(newResponse);
                    subrecords[record.FormKey].Add(newResponse);
                }

            }
            foreach (var record in records)
            {
                var name = record.Name?.String;
                var grup = record.Responses;
                var speeches = grup.Where(r => GetSpeechCondition(r) != null).ToList();
                var speechValues = speeches.Select(info => GetSpeechValue(GetSpeechCondition(info)!)).ToList();
                var newResponses = new Dictionary<FormKey, DialogResponses> { };
                if (name is not null && !record.FormKey.Equals(FormKey.Factory("027F41:Skyrim.esm")) && (speeches.Count == 1 || !speechValues.Any(s => s != speechValues[0])))
                {
                    if (name.Contains("Persuade"))
                        record.Name = name.Replace("(Persuade)", $"(Persuade: {speechValues[0]})");
                    else if (!name.Contains("(Intimidate)") && !name.Contains("gold)"))
                        record.Name += $" (Persuade: {speechValues[0]})";
                }
                foreach (var info in grup)
                {
                    int responseIndex = ListExt.FindIndex<DialogResponses, DialogResponses>(subrecords[record.FormKey], s => s.FormKey == info.FormKey);
                    if (info.PreviousDialog.IsNull && responseIndex != 0)
                        info.PreviousDialog = new FormLinkNullable<IDialogResponsesGetter>(subrecords[record.FormKey][responseIndex - 1]);
                    if (GetSpeechCondition(info.Conditions, out var speech))
                    {
                        speech!.Data.RunOnType = RunOnType.Reference;
                        speech!.Data.Reference = Skyrim.PlayerRef;
                    }
                    if (speech is not null && GetAmuletCondition(info) is null)
                    {
                        if (info.Conditions.Last().Equals(speech))
                            info.Conditions.Add(ConstructAmulet());
                        else
                            info.Conditions.Insert(info.Conditions.IndexOf(speech) + 1, ConstructAmulet());
                    }
                    if (info.FormKey.Equals(FormKey.Factory("0E7752:Skyrim.esm")) || info.FormKey.Equals(FormKey.Factory("04DDAA:Skyrim.esm")))
                        info.Prompt = null;
                    if (info.FormKey.Equals(FormKey.Factory("02B8BD:Skyrim.esm")))
                    {
                        var conditionData = (GetRelationshipRankConditionData)info.Conditions.First().Data;
                        conditionData.TargetNpc.Link.SetTo(Skyrim.PlacedNpc.FalkFirebeardREF);
                    }
                    if (info.FormKey.Equals(FormKey.Factory("02B8BE:Skyrim.esm")))
                        info.Prompt = "Falk asked me to check it out.";
                    if (info.FormKey.Equals(FormKey.Factory("027F63:Skyrim.esm")))
                        info.Conditions.RemoveAt(0);
                    if (speeches.Count != 0)
                        speech ??= GetSpeechCondition(speeches.LastOrDefault(s => grup.IndexOf(s) <= grup.IndexOf(info)));
                    if (info.Prompt == null && name is not null && speeches.Count > 1 && speechValues.Any(s => s != speechValues[0]))
                    {
                        if (name.Contains("(Persuade)"))
                            info.Prompt = name.Replace("(Persuade)", $"(Persuade: {GetSpeechValue(speech!)})");
                        else
                            info.Prompt = name + $"(Persuade: {GetSpeechValue(speech!)})";
                    }
                    if (!(name is not null && name.Contains("(Persuade:")) && info.Prompt is not null && info.Prompt.String is not null && info.Prompt.String.Contains("(Persuade)"))
                    {
                        info.Prompt.String = info.Prompt.String.Replace("(Persuade)", $"(Persuade: {GetSpeechValue(speech!)})");
                    }
                    if (info.FormKey.Equals(FormKey.Factory("04FA11:Skyrim.esm")))
                    {
                        var newResponse = info.Duplicate(state.PatchMod.GetNextFormKey());
                        newResponse.VirtualMachineAdapter!.Clear();
                        newResponse.Flags!.Flags |= DialogResponses.Flag.Goodbye;
                        newResponse.PreviousDialog = new FormLinkNullable<IDialogResponsesGetter>(info);
                        newResponse.Conditions.RemoveAt(2);
                        newResponse.Conditions.RemoveAt(2);
                        newResponse.ResponseData.FormKey = FormKey.Factory("0E0CC4:Skyrim.esm");
                        newResponse.Responses.Clear();
                        newResponses.Add(info.FormKey, newResponse);
                    }
                }
                foreach (var info in newResponses)
                {
                    if (record.Responses.Last().FormKey == info.Key)
                        record.Responses.Add(info.Value);
                    else
                        record.Responses.Insert(record.Responses.FindIndex(s => s.FormKey == info.Key) + 1, info.Value);
                }
            }
        }
    }
}