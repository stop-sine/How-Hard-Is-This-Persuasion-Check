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


        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "HHITPC.esp")
                .Run(args);
        }


        public static bool GetAmuletCondition(DialogResponses info, out ConditionFloat? amulet)
        {
            Condition condition = info.Conditions.FirstOrDefault(c => c.Data is GetEquippedConditionData e && e.ItemOrList.Link.Equals(Skyrim.FormList.TGAmuletofArticulationList))!;
            amulet = condition as ConditionFloat;
            return amulet != null;
        }

        public static bool GetAmuletCondition(IDialogResponsesGetter info, out IConditionFloatGetter? condition)
        {
            IConditionGetter foundCondition = info.Conditions.FirstOrDefault(c => c.Data is GetEquippedConditionData e && e.ItemOrList.Link.Equals(Skyrim.FormList.TGAmuletofArticulationList))!;
            condition = foundCondition as IConditionFloatGetter;
            return condition != null;
        }

        public static bool GetAmuletResponses(ExtendedList<DialogResponses> grup, out List<DialogResponses> subrecords)
        {
            subrecords = [.. grup.Where(subrecord => GetAmuletCondition(subrecord, out _))];
            return subrecords.Count > 0;
        }

        public static bool GetAmuletResponses(IReadOnlyList<IDialogResponsesGetter> grup, out List<IDialogResponsesGetter> subrecords)
        {
            subrecords = [.. grup.Where(subrecord => GetAmuletCondition(subrecord, out _))];
            return subrecords.Count > 0;
        }

        public static string GetSpeechValue(Condition condition)
        {
            return SpeechValues[condition.Cast<ConditionGlobal>().ComparisonValue.FormKey];
        }

        public static string GetSpeechValue(IConditionGetter condition)
        {
            return SpeechValues[condition.Cast<IConditionGlobalGetter>().ComparisonValue.FormKey];
        }

        public static IConditionGetter? GetSpeechCondition(IDialogResponsesGetter? info)
        {
            return info?.Conditions.FirstOrDefault(c => c is IConditionGlobalGetter global && SpeechGlobals.Contains(global.ComparisonValue.FormKey));
        }

        public static Condition? GetSpeechCondition(DialogResponses? info)
        {
            return info?.Conditions.FirstOrDefault(c => c is ConditionGlobal global && SpeechGlobals.Contains(global.ComparisonValue.FormKey));
        }

        public static bool GetSpeechCondition(DialogResponses info, out Condition? condition)
        {
            condition = info.Conditions.FirstOrDefault(c => c.DeepCopy() is ConditionGlobal global && SpeechGlobals.Contains(global.ComparisonValue.FormKey));
            return condition != null;
        }

        public static bool GetSpeechCondition(IDialogResponsesGetter info, out IConditionGetter? condition)
        {
            condition = info.Conditions.FirstOrDefault(c => c is IConditionGlobalGetter global && SpeechGlobals.Contains(global.ComparisonValue.FormKey));
            return condition != null;
        }

        public static bool GetSpeechCondition(IReadOnlyList<IConditionGetter> conditions, out IConditionGetter? condition)
        {
            condition = conditions.FirstOrDefault(c => c is IConditionGlobalGetter global && SpeechGlobals.Contains(global.ComparisonValue.FormKey));
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

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var cache = state.LinkCache;
            var records = new List<IDialogTopicGetter>();
            var subrecords = new Dictionary<FormKey, List<IDialogResponsesGetter>>();
            foreach (var record in state.LoadOrder.PriorityOrder.DialogTopic().WinningOverrides().Where(r => GetSpeechResponses(r.Responses) != null))
                records.Add(record);
            Console.WriteLine($"Found {records.Count} Dialog Topic records with persuasion checks");
            foreach (var record in records)
                Console.WriteLine(record.EditorID);
            foreach (var record in records)
            {
                var responses = new List<IDialogResponsesGetter>();
                GetSpeechResponses(record.Responses, out var speechResponses);
                foreach (var info in record.Responses)
                {
                    if (speechResponses.Contains(info)
                        || (info.PreviousDialog!.IsNull && record.Responses.IndexOf(info) != 0)
                        || (info.Prompt?.String?.Contains("(Persuade)") == true)
                        || (info.Prompt is null && speechResponses.Count > 1 && !speechResponses.Any(r => GetSpeechCondition(r) != GetSpeechCondition(speechResponses[0])))
                        || info.FormKey.Equals(FormKey.Factory("0E7752:Skyrim.esm"))
                        || info.FormKey.Equals(FormKey.Factory("04DDAA:Skyrim.esm"))
                        || info.FormKey.Equals(FormKey.Factory("02B8BD:Skyrim.esm")))
                    {
                        responses.Add(info);
                    }
                }
                subrecords.Add(record.FormKey, responses);
            }
            foreach (var record in records)
            {
                var dial = state.PatchMod.DialogTopics.GetOrAddAsOverride(record);
                var grup = dial.Responses;
                foreach (var info in subrecords[record.FormKey])
                    grup.Add(info.DeepCopy());
                var speeches = grup.Where(r => GetSpeechCondition(r) != null).ToList();
                if (dial.Name != null && dial.Name.String != null && (speeches.Count == 1 || !speeches.Any(r => GetSpeechValue(GetSpeechCondition(r)!) != GetSpeechValue(GetSpeechCondition(speeches[0])!))))
                {
                    if (dial.Name.String.Contains("Persuade"))
                        dial.Name.String = dial.Name.String.Replace("(Persuade)", $"(Persuade: {GetSpeechValue(GetSpeechCondition(speeches[0])!)})");
                    else
                        dial.Name.String = dial.Name + $" (Persuade: {GetSpeechValue(GetSpeechCondition(speeches[0])!)})";
                }
                foreach (var info in grup)
                {
                    int responseIndex = ListExt.FindIndex<IDialogResponsesGetter, IDialogResponsesGetter>(record.Responses, s => s.FormKey == info.FormKey);
                    if (info.PreviousDialog.IsNull && responseIndex != 0)
                        info.PreviousDialog = new FormLinkNullable<IDialogResponsesGetter>(record.Responses[responseIndex - 1]);
                    if (GetSpeechCondition(info.Conditions, out var speech))
                    {
                        speech!.Data.RunOnType = RunOnType.Reference;
                        speech!.Data.Reference = Skyrim.PlayerRef;
                    }
                    if (speech is not null && !GetAmuletCondition(info, out var amulet))
                    {
                        amulet = new ConditionFloat
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
                        if (info.Conditions.Last().Equals(speech))
                            info.Conditions.Add(amulet);
                        else
                            info.Conditions.Insert(info.Conditions.IndexOf(speech) + 1, amulet);
                    }
                    if (info.FormKey.Equals(FormKey.Factory("0E7752:Skyrim.esm")) || info.FormKey.Equals(FormKey.Factory("04DDAA:Skyrim.esm")))
                        info.Prompt!.Clear();
                    if (info.FormKey.Equals(FormKey.Factory("02B8BD:Skyrim.esm")))
                    {
                        var conditionData = (GetRelationshipRankConditionData)info.Conditions.First().Data;
                        conditionData.TargetNpc.Link.SetTo(Skyrim.PlacedNpc.FalkFirebeardREF);
                    }
                    if (speeches.Count != 0)
                        speech ??= GetSpeechCondition(speeches.Last());
                    if (info.Prompt == null && dial.Name != null && dial.Name.String != null && speeches.Count > 1 && speeches.Any(r => GetSpeechValue(GetSpeechCondition(r)!) != GetSpeechValue(GetSpeechCondition(speeches[0])!)))
                    {
                        if (dial.Name.String.Contains("(Persuade)"))
                            info.Prompt = dial.Name.String.Replace("(Persuade)", $"(Persuade: {GetSpeechValue(speech!)})");
                        else
                            info.Prompt = dial.Name + $"(Persuade: {GetSpeechValue(speech!)})";
                    }
                    if (!(dial.Name != null && dial.Name.String != null && dial.Name!.String!.Contains("(Persuade:")) && info.Prompt is not null && info.Prompt.String is not null && info.Prompt.String.Contains("(Persuade)"))
                        info.Prompt.String = info.Prompt.String.Replace("(Persuade)", $"(Persuade: {GetSpeechValue(speech!)})");
                }
            }
        }
    }
}