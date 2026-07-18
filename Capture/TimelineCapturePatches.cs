// SPDX-License-Identifier: MPL-2.0

using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib;
using STS2RitsuLib.Patching.Builders;
using STS2RitsuLib.Patching.Core;

namespace STS2RitsuMetrics.Capture
{
    internal static class TimelineCapturePatches
    {
        private static readonly string[] ModifierNames =
        [
            "ModifyDamageAdditive",
            "ModifyDamageMultiplicative",
            "ModifyDamageCap",
            "EnchantDamageAdditive",
            "EnchantDamageMultiplicative",
            "ModifyHpLostBeforeOsty",
            "ModifyHpLostBeforeOstyLate",
            "ModifyHpLostAfterOsty",
            "ModifyHpLostAfterOstyLate",
        ];

        private static readonly string[] FactorModifierNames =
        [
            "ModifyVulnerableMultiplier",
            "ModifyWeakMultiplier",
        ];

        private static bool _initialized;
        private static readonly HashSet<MethodBase> PatchedModelMethods = [];
        private static ModPatcher? _patcher;
        private static int _skippedDamageCalculations;
        private static int _skippedDamageRequests;
        private static int _skippedDoomExecutions;
        private static int _skippedExplicitKills;
        private static int _skippedHpLossCalculations;
        private static int _skippedModelHooks;
        private static int _skippedModifiers;

        internal static void Initialize()
        {
            if (_initialized)
                return;

            var builder = new DynamicPatchBuilder("timeline_capture");
            AddDamageRequestPatch(builder);
            AddDamageCalculationPatch(builder);
            AddHpLossCalculationPatch(builder);
            AddDoomExecutionPatch(builder);
            AddExplicitKillPatch(builder);
            AddModelPatches(builder);

            _patcher = RitsuLibFramework.CreatePatcher(ModConstants.ModId, "timeline-capture",
                "causal timeline capture");
            if (!_patcher.ApplyDynamic(builder, true))
                throw new InvalidOperationException(
                    "One or more critical timeline capture patches could not be applied.");

            _initialized = true;
            Main.Logger.Info($"Installed {builder.Patches.Count} adaptive timeline capture patches.");
        }

        internal static void RefreshModelPatches()
        {
            if (!_initialized || _patcher == null)
                return;
            var builder = new DynamicPatchBuilder("timeline_capture_late");
            AddModelPatches(builder);
            if (builder.Patches.Count == 0)
                return;
            if (!_patcher.ApplyDynamic(builder))
                Main.Logger.Warn("Some optional late-loaded model timeline patches could not be applied.");
            Main.Logger.Info($"Installed {builder.Patches.Count} late-loaded model timeline capture patches.");
        }

        internal static SkippedOriginalDiagnostics ConsumeSkippedOriginalDiagnostics()
        {
            return new(
                Interlocked.Exchange(ref _skippedDamageRequests, 0),
                Interlocked.Exchange(ref _skippedDamageCalculations, 0),
                Interlocked.Exchange(ref _skippedHpLossCalculations, 0),
                Interlocked.Exchange(ref _skippedDoomExecutions, 0),
                Interlocked.Exchange(ref _skippedExplicitKills, 0),
                Interlocked.Exchange(ref _skippedModelHooks, 0),
                Interlocked.Exchange(ref _skippedModifiers, 0));
        }

        private static void AddDamageRequestPatch(DynamicPatchBuilder builder)
        {
            var method = typeof(CreatureCmd).GetMethods(BindingFlags.Public | BindingFlags.Static)
                             .Where(candidate => candidate.Name == nameof(CreatureCmd.Damage))
                             .Where(candidate => IsDamageTask(candidate.ReturnType))
                             .Select(candidate => (Method: candidate, Parameters: candidate.GetParameters()))
                             .Where(candidate => candidate.Parameters.Any(parameter => parameter.Name == "targets"))
                             .Where(candidate => candidate.Parameters.Any(parameter => parameter.Name == "amount"))
                             .OrderByDescending(candidate => candidate.Parameters.Length)
                             .Select(candidate => candidate.Method)
                             .FirstOrDefault()
                         ?? throw new MissingMethodException(typeof(CreatureCmd).FullName,
                             "Damage targets/amount overload");

            builder.Add(
                method,
                CapturePatchMethod(nameof(DamageRequestPrefix), Priority.First),
                CapturePatchMethod(nameof(DamageRequestPostfix), Priority.Last),
                finalizer: CapturePatchMethod(nameof(DamageRequestFinalizer), Priority.Last),
                isCritical: true,
                description: "Capture the causal scope of actual damage requests");
        }

        private static void AddDamageCalculationPatch(DynamicPatchBuilder builder)
        {
            var method = typeof(Hook).GetMethods(BindingFlags.Public | BindingFlags.Static)
                             .Where(candidate =>
                                 candidate.Name == nameof(Hook.ModifyDamage) && candidate.ReturnType == typeof(decimal))
                             .Select(candidate => (Method: candidate, Parameters: candidate.GetParameters()))
                             .Where(candidate => candidate.Parameters.Any(parameter => parameter.Name == "damage"))
                             .Where(candidate => candidate.Parameters.Any(parameter => parameter.IsOut))
                             .OrderByDescending(candidate => candidate.Parameters.Length)
                             .Select(candidate => candidate.Method)
                             .FirstOrDefault()
                         ?? throw new MissingMethodException(typeof(Hook).FullName, nameof(Hook.ModifyDamage));

            builder.Add(
                method,
                CapturePatchMethod(nameof(DamageCalculationPrefix), Priority.First),
                CapturePatchMethod(nameof(DamageCalculationPostfix), Priority.Last),
                finalizer: CapturePatchMethod(nameof(DamageCalculationFinalizer), Priority.Last),
                isCritical: true,
                description: "Capture the exact damage modifier waterfall");
        }

        private static void AddHpLossCalculationPatch(DynamicPatchBuilder builder)
        {
            var method = typeof(Hook).GetMethods(BindingFlags.Public | BindingFlags.Static)
                             .Where(candidate =>
                                 candidate.Name == nameof(Hook.ModifyHpLost) && candidate.ReturnType == typeof(decimal))
                             .Select(candidate => (Method: candidate, Parameters: candidate.GetParameters()))
                             .Where(candidate => candidate.Parameters.Any(parameter => parameter.Name == "amount"))
                             .Where(candidate => candidate.Parameters.Any(parameter => parameter.IsOut))
                             .OrderByDescending(candidate => candidate.Parameters.Length)
                             .Select(candidate => candidate.Method)
                             .FirstOrDefault()
                         ?? throw new MissingMethodException(typeof(Hook).FullName, nameof(Hook.ModifyHpLost));

            builder.Add(
                method,
                CapturePatchMethod(nameof(HpLossCalculationPrefix), Priority.First),
                CapturePatchMethod(nameof(HpLossCalculationPostfix), Priority.Last),
                finalizer: CapturePatchMethod(nameof(HpLossCalculationFinalizer), Priority.Last),
                isCritical: true,
                description: "Capture effects that modify actual HP loss");
        }

        private static void AddDoomExecutionPatch(DynamicPatchBuilder builder)
        {
            var type = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Powers.DoomPower");
            var method = type?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(candidate => candidate.Name == "DoomKill" && candidate.ReturnType == typeof(Task));
            if (method == null)
                return;
            ExecutionCaptureHub.ConfigureDoomType(type!);
            builder.Add(
                method,
                CapturePatchMethod(nameof(DoomExecutionPrefix), Priority.First),
                CapturePatchMethod(nameof(DoomExecutionPostfix), Priority.Last),
                finalizer: CapturePatchMethod(nameof(DoomExecutionFinalizer), Priority.Last),
                isCritical: false,
                description: "Mark direct Doom executions without modifying their behavior");
        }

        private static void AddExplicitKillPatch(DynamicPatchBuilder builder)
        {
            var method = typeof(CreatureCmd).GetMethods(BindingFlags.Public | BindingFlags.Static)
                             .Where(candidate => candidate.Name == nameof(CreatureCmd.Kill) &&
                                                 candidate.ReturnType == typeof(Task))
                             .Select(candidate => (Method: candidate, Parameters: candidate.GetParameters()))
                             .Where(candidate => candidate.Parameters.Length > 0 &&
                                                 typeof(IEnumerable<Creature>).IsAssignableFrom(
                                                     candidate.Parameters[0].ParameterType))
                             .OrderByDescending(candidate => candidate.Parameters.Length)
                             .Select(candidate => candidate.Method)
                             .FirstOrDefault()
                         ?? throw new MissingMethodException(typeof(CreatureCmd).FullName,
                             "Kill creature collection overload");
            builder.Add(
                method,
                CapturePatchMethod(nameof(ExplicitKillPrefix), Priority.First),
                CapturePatchMethod(nameof(ExplicitKillPostfix), Priority.Last),
                finalizer: CapturePatchMethod(nameof(ExplicitKillFinalizer), Priority.Last),
                isCritical: true,
                description: "Distinguish explicit death cleanup from attributable HP loss");
        }

        private static void AddModelPatches(DynamicPatchBuilder builder)
        {
            var baseHooks = typeof(AbstractModel)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.IsVirtual && method.ReturnType == typeof(Task))
                .Select(method => method.GetBaseDefinition())
                .ToHashSet();
            var modelPrefix = CapturePatchMethod(nameof(ModelHookPrefix), Priority.First);
            var modelPostfix = CapturePatchMethod(nameof(ModelHookPostfix), Priority.Last);
            var modelFinalizer = CapturePatchMethod(nameof(ModelHookFinalizer), Priority.Last);
            var modifierPrefix = CapturePatchMethod(nameof(ModifierPrefix), Priority.First);
            var modifierPostfix = CapturePatchMethod(nameof(ModifierPostfix), Priority.Last);
            var factorModifierPrefix = CapturePatchMethod(nameof(FactorModifierPrefix), Priority.First);
            var factorModifierPostfix = CapturePatchMethod(nameof(FactorModifierPostfix), Priority.Last);

            foreach (var type in GetLoadableTypes().Where(type =>
                         !type.IsAbstract && typeof(AbstractModel).IsAssignableFrom(type)))
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method.IsAbstract || method.ContainsGenericParameters)
                    continue;
                if (method.ReturnType == typeof(Task) && baseHooks.Contains(method.GetBaseDefinition()))
                {
                    if (!PatchedModelMethods.Add(method))
                        continue;
                    builder.Add(
                        method,
                        modelPrefix,
                        modelPostfix,
                        finalizer: modelFinalizer,
                        isCritical: false,
                        description: $"Create a deferred causal scope for {type.FullName}.{method.Name}");
                    continue;
                }

                if (method.ReturnType == typeof(decimal) && ModifierNames.Contains(method.Name, StringComparer.Ordinal))
                {
                    if (!PatchedModelMethods.Add(method))
                        continue;
                    builder.Add(
                        method,
                        modifierPrefix,
                        modifierPostfix,
                        isCritical: false,
                        description: $"Capture damage contribution from {type.FullName}.{method.Name}");
                }
                else if (method.ReturnType == typeof(decimal) &&
                         FactorModifierNames.Contains(method.Name, StringComparer.Ordinal))
                {
                    if (!PatchedModelMethods.Add(method))
                        continue;
                    builder.Add(
                        method,
                        factorModifierPrefix,
                        factorModifierPostfix,
                        isCritical: false,
                        description: $"Capture nested multiplier contribution from {type.FullName}.{method.Name}");
                }
            }
        }

        private static IEnumerable<Type> GetLoadableTypes()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types.OfType<Type>().ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                    yield return type;
            }
        }

        private static bool IsDamageTask(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>) &&
                   typeof(IEnumerable<DamageResult>).IsAssignableFrom(type.GenericTypeArguments[0]);
        }

        private static HarmonyMethod CapturePatchMethod(string name, int priority)
        {
            var method = DynamicPatchBuilder.FromMethod(typeof(TimelineCapturePatches), name);
            method.priority = priority;
            return method;
        }

        private static void DamageRequestPrefix(MethodBase __originalMethod, object[] __args,
            out DamageCaptureHub.RequestState? __state)
        {
            __state = DamageCaptureHub.BeginRequest(__originalMethod, __args);
        }

        private static void DamageRequestPostfix(ref Task<IEnumerable<DamageResult>> __result, bool __runOriginal,
            DamageCaptureHub.RequestState? __state)
        {
            if (!__runOriginal && __state != null)
                Interlocked.Increment(ref _skippedDamageRequests);
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (__state != null && __result != null)
                __result = CompleteDamageRequest(__result, __state.Request);
            DamageCaptureHub.RestoreRequest(__state);
        }

        private static async Task<IEnumerable<DamageResult>> CompleteDamageRequest(
            Task<IEnumerable<DamageResult>> resultTask,
            DamageRequestCapture request)
        {
            var results = await resultTask;
            if (results is IReadOnlyList<DamageResult> materialized)
                DamageCaptureHub.CompleteRequest(request, materialized);
            return results;
        }

        private static Exception? DamageRequestFinalizer(Exception? __exception, DamageCaptureHub.RequestState? __state)
        {
            DamageCaptureHub.RestoreRequest(__state);
            return __exception;
        }

        private static void DamageCalculationPrefix(MethodBase __originalMethod, object[] __args,
            out DamageCaptureHub.CalculationState? __state)
        {
            __state = DamageCaptureHub.BeginCalculation(__originalMethod, __args);
        }

        private static void DamageCalculationPostfix(decimal __result, bool __runOriginal,
            DamageCaptureHub.CalculationState? __state)
        {
            if (__runOriginal)
            {
                DamageCaptureHub.CompleteCalculation(__state, __result);
            }
            else
            {
                if (__state != null)
                    Interlocked.Increment(ref _skippedDamageCalculations);
                DamageCaptureHub.RestoreCalculation(__state);
            }
        }

        private static Exception? DamageCalculationFinalizer(Exception? __exception,
            DamageCaptureHub.CalculationState? __state)
        {
            DamageCaptureHub.RestoreCalculation(__state);
            return __exception;
        }

        private static void HpLossCalculationPrefix(MethodBase __originalMethod, object[] __args,
            out DamageCaptureHub.HpLossState? __state)
        {
            __state = DamageCaptureHub.BeginHpLoss(__originalMethod, __args);
        }

        private static void HpLossCalculationPostfix(decimal __result, bool __runOriginal,
            DamageCaptureHub.HpLossState? __state)
        {
            if (__runOriginal)
            {
                DamageCaptureHub.CompleteHpLoss(__state, __result);
            }
            else
            {
                if (__state != null)
                    Interlocked.Increment(ref _skippedHpLossCalculations);
                DamageCaptureHub.RestoreHpLoss(__state);
            }
        }

        private static Exception? HpLossCalculationFinalizer(Exception? __exception,
            DamageCaptureHub.HpLossState? __state)
        {
            DamageCaptureHub.RestoreHpLoss(__state);
            return __exception;
        }

        private static void DoomExecutionPrefix(object __0, out ExecutionCaptureHub.State? __state)
        {
            __state = ExecutionCaptureHub.BeginDoom(__0);
        }

        private static void DoomExecutionPostfix(bool __runOriginal, ExecutionCaptureHub.State? __state)
        {
            if (!__runOriginal && __state != null)
                Interlocked.Increment(ref _skippedDoomExecutions);
            ExecutionCaptureHub.Restore(__state);
        }

        private static Exception? DoomExecutionFinalizer(Exception? __exception, ExecutionCaptureHub.State? __state)
        {
            ExecutionCaptureHub.Restore(__state);
            return __exception;
        }

        private static void ExplicitKillPrefix(object __0, out ExecutionCaptureHub.KillState? __state)
        {
            __state = ExecutionCaptureHub.BeginExplicitKill(__0);
        }

        private static void ExplicitKillPostfix(bool __runOriginal, ExecutionCaptureHub.KillState? __state)
        {
            if (!__runOriginal && __state != null)
                Interlocked.Increment(ref _skippedExplicitKills);
            ExecutionCaptureHub.RestoreKill(__state);
        }

        private static Exception? ExplicitKillFinalizer(Exception? __exception,
            ExecutionCaptureHub.KillState? __state)
        {
            ExecutionCaptureHub.RestoreKill(__state);
            return __exception;
        }

        private static void ModelHookPrefix(AbstractModel __instance, MethodBase __originalMethod,
            out CausalScopeRuntime.ScopeState? __state)
        {
            __state = CausalScopeRuntime.EnterModel(__instance, __originalMethod.Name);
        }

        private static void ModelHookPostfix(bool __runOriginal, CausalScopeRuntime.ScopeState? __state)
        {
            if (!__runOriginal && __state != null)
                Interlocked.Increment(ref _skippedModelHooks);
            CausalScopeRuntime.Restore(__state);
        }

        private static Exception? ModelHookFinalizer(Exception? __exception, CausalScopeRuntime.ScopeState? __state)
        {
            CausalScopeRuntime.Restore(__state);
            return __exception;
        }

        private static void ModifierPrefix(AbstractModel __instance, MethodBase __originalMethod,
            out DamageCaptureHub.ModifierState? __state)
        {
            __state = DamageCaptureHub.BeginModifier(__instance, __originalMethod);
        }

        private static void ModifierPostfix(decimal __result, bool __runOriginal,
            DamageCaptureHub.ModifierState? __state)
        {
            if (__runOriginal)
                DamageCaptureHub.CompleteModifier(__state, __result);
            else if (__state != null)
                Interlocked.Increment(ref _skippedModifiers);
        }

        private static void FactorModifierPrefix(AbstractModel __instance, MethodBase __originalMethod, object[] __args,
            out DamageCaptureHub.FactorModifierState? __state)
        {
            __state = DamageCaptureHub.BeginFactorModifier(__instance, __originalMethod, __args);
        }

        private static void FactorModifierPostfix(decimal __result, bool __runOriginal,
            DamageCaptureHub.FactorModifierState? __state)
        {
            if (__runOriginal)
                DamageCaptureHub.CompleteFactorModifier(__state, __result);
            else if (__state != null)
                Interlocked.Increment(ref _skippedModifiers);
        }
    }

    internal readonly record struct SkippedOriginalDiagnostics(
        int DamageRequests,
        int DamageCalculations,
        int HpLossCalculations,
        int DoomExecutions,
        int ExplicitKills,
        int ModelHooks,
        int Modifiers)
    {
        internal int Total => DamageRequests + DamageCalculations + HpLossCalculations + DoomExecutions +
                              ExplicitKills + ModelHooks + Modifiers;
    }
}
