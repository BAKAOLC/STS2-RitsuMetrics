// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;

namespace STS2RitsuMetrics.Capture
{
    internal sealed record ModifierCapture(
        SourceDescriptor Source,
        DamageContributionStage Stage,
        decimal InputValue,
        decimal OutputValue,
        decimal RawContribution,
        decimal? Factor,
        AbstractModel Model,
        string Context);

    internal sealed record FactorModifierCapture(
        SourceDescriptor Source,
        decimal InputFactor,
        decimal OutputFactor,
        AbstractModel Model,
        string Context);

    internal sealed class DamageCalculationCapture(
        Creature? target,
        Creature? dealer,
        CardModel? cardSource,
        decimal requestedAmount,
        ValueProp props,
        CausalScopeSnapshot? cause)
    {
        internal Creature? Target { get; } = target;
        internal Creature? Dealer { get; } = dealer;
        internal CardModel? CardSource { get; } = cardSource;
        internal decimal RequestedAmount { get; } = requestedAmount;
        internal ValueProp Props { get; } = props;
        internal CausalScopeSnapshot? Cause { get; } = cause;
        internal decimal CurrentValue { get; set; } = requestedAmount;
        internal decimal ModifiedAmount { get; set; } = requestedAmount;
        internal decimal? FinalHpLossAmount { get; set; }
        internal List<ModifierCapture> Modifiers { get; } = [];
        internal List<FactorModifierCapture> FactorModifiers { get; } = [];
        internal bool Consumed { get; set; }
    }

    internal sealed class DamageRequestCapture(
        decimal requestedAmount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        IReadOnlyList<Creature> targets,
        CausalScopeSnapshot? cause)
    {
        internal decimal RequestedAmount { get; } = requestedAmount;
        internal ValueProp Props { get; } = props;
        internal Creature? Dealer { get; } = dealer;
        internal CardModel? CardSource { get; } = cardSource;
        internal IReadOnlyList<Creature> Targets { get; } = targets;
        internal CausalScopeSnapshot? Cause { get; } = cause;
        internal List<DamageCalculationCapture> Calculations { get; } = [];
    }

    internal static class DamageCaptureHub
    {
        private static readonly AsyncLocal<DamageRequestCapture?> CurrentRequestValue = new();
        private static readonly AsyncLocal<DamageCalculationCapture?> CurrentCalculationValue = new();
        private static readonly ConcurrentDictionary<MethodBase, ArgumentLayout> ArgumentLayouts = new();

        internal static bool HasActiveRequest => CurrentRequestValue.Value != null;

        internal static RequestState? BeginRequest(MethodBase method, object[] args)
        {
            if (!CaptureBridge.Active)
                return null;
            var layout = Layout(method);
            var amount = Argument<decimal>(args, layout.AmountIndex);
            var props = Argument<ValueProp>(args, layout.PropsIndex);
            var dealer = Argument<Creature>(args, layout.DealerIndex);
            var card = Argument<CardModel>(args, layout.CardSourceIndex);
            var targets = Targets(args, layout);
            var previous = CurrentRequestValue.Value;
            var request = new DamageRequestCapture(amount, props, dealer, card, targets, CausalScopeRuntime.Snapshot());
            CurrentRequestValue.Value = request;
            return new(request, previous);
        }

        internal static void RestoreRequest(RequestState? state)
        {
            if (state != null && ReferenceEquals(CurrentRequestValue.Value, state.Request))
                CurrentRequestValue.Value = state.Previous;
        }

        internal static CalculationState? BeginCalculation(MethodBase method, object[] args)
        {
            var request = CurrentRequestValue.Value;
            if (request == null)
                return null;
            var layout = Layout(method);
            var target = Argument<Creature>(args, layout.TargetIndex);
            var dealer = Argument<Creature>(args, layout.DealerIndex) ?? request.Dealer;
            var card = Argument<CardModel>(args, layout.CardSourceIndex) ?? request.CardSource;
            var amount = Argument<decimal>(args, layout.DamageIndex);
            var props = Argument<ValueProp>(args, layout.PropsIndex);
            var previous = CurrentCalculationValue.Value;
            var calculation = new DamageCalculationCapture(target, dealer, card, amount, props, request.Cause);
            CurrentCalculationValue.Value = calculation;
            return new(request, calculation, previous);
        }

        internal static void CompleteCalculation(CalculationState? state, decimal result)
        {
            if (state == null)
                return;
            state.Calculation.ModifiedAmount = result;
            state.Calculation.CurrentValue = result;
            lock (state.Request.Calculations)
            {
                state.Request.Calculations.Add(state.Calculation);
            }

            RestoreCalculation(state);
        }

        internal static void RestoreCalculation(CalculationState? state)
        {
            if (state != null && ReferenceEquals(CurrentCalculationValue.Value, state.Calculation))
                CurrentCalculationValue.Value = state.Previous;
        }

        internal static ModifierState? BeginModifier(AbstractModel model, MethodBase method)
        {
            var calculation = CurrentCalculationValue.Value;
            if (calculation == null)
                return null;
            var input = calculation.CurrentValue;
            return new(calculation, model, Layout(method).MethodName, input, calculation.FactorModifiers.Count);
        }

        internal static FactorModifierState? BeginFactorModifier(AbstractModel model, MethodBase method, object[] args)
        {
            var calculation = CurrentCalculationValue.Value;
            if (calculation == null)
                return null;
            var layout = Layout(method);
            var amount = Argument<decimal>(args, layout.AmountIndex);
            return new(calculation, model, layout.MethodName, amount);
        }

        internal static void CompleteFactorModifier(FactorModifierState? state, decimal result)
        {
            if (state == null || result == state.InputFactor)
                return;
            state.Calculation.FactorModifiers.Add(new(
                GameDescriptorFactory.Model(state.Model),
                state.InputFactor,
                result,
                state.Model,
                state.MethodName));
        }

        internal static HpLossState? BeginHpLoss(MethodBase method, object[] args)
        {
            var request = CurrentRequestValue.Value;
            if (request == null)
                return null;
            var layout = Layout(method);
            var target = Argument<Creature>(args, layout.TargetIndex);
            var props = Argument<ValueProp>(args, layout.PropsIndex);
            var amount = Argument<decimal>(args, layout.AmountIndex);
            DamageCalculationCapture? calculation;
            lock (request.Calculations)
            {
                calculation = request.Calculations.LastOrDefault(candidate =>
                    ReferenceEquals(candidate.Target, target) && candidate.Props == props);
                calculation ??= request.Calculations.LastOrDefault(candidate => candidate.Props == props);
            }

            if (calculation == null)
                return null;
            var previous = CurrentCalculationValue.Value;
            calculation.CurrentValue = amount;
            CurrentCalculationValue.Value = calculation;
            return new(calculation, previous);
        }

        internal static void CompleteHpLoss(HpLossState? state, decimal result)
        {
            if (state == null)
                return;
            state.Calculation.FinalHpLossAmount = result;
            state.Calculation.CurrentValue = result;
            RestoreHpLoss(state);
        }

        internal static void RestoreHpLoss(HpLossState? state)
        {
            if (state != null && ReferenceEquals(CurrentCalculationValue.Value, state.Calculation))
                CurrentCalculationValue.Value = state.Previous;
        }

        internal static void CompleteModifier(ModifierState? state, decimal result)
        {
            if (state == null)
                return;
            var calculation = state.Calculation;
            var stage = state.MethodName switch
            {
                "ModifyDamageAdditive" or "EnchantDamageAdditive" => DamageContributionStage.Additive,
                "ModifyDamageMultiplicative" or "EnchantDamageMultiplicative" => DamageContributionStage.Multiplicative,
                "ModifyDamageCap" => DamageContributionStage.Cap,
                _ => DamageContributionStage.HpLoss,
            };
            decimal output;
            decimal contribution;
            decimal? factor = null;
            switch (stage)
            {
                case DamageContributionStage.Additive:
                    output = state.InputValue + result;
                    contribution = result;
                    break;
                case DamageContributionStage.Multiplicative:
                    var factorModifiers = calculation.FactorModifiers.Skip(state.FactorModifierStartIndex).ToArray();
                    if (factorModifiers.Length > 0)
                    {
                        var initialFactor = factorModifiers[0].InputFactor;
                        AddModifier(calculation, state.Model, DamageContributionStage.Multiplicative,
                            state.InputValue, state.InputValue * initialFactor,
                            state.InputValue * (initialFactor - 1m), initialFactor, state.MethodName);
                        foreach (var nested in factorModifiers)
                            AddModifier(calculation, nested.Model, DamageContributionStage.Multiplicative,
                                state.InputValue * nested.InputFactor, state.InputValue * nested.OutputFactor,
                                state.InputValue * (nested.OutputFactor - nested.InputFactor),
                                nested.OutputFactor / (nested.InputFactor == 0m ? 1m : nested.InputFactor),
                                nested.Context);
                        var lastFactor = factorModifiers[^1].OutputFactor;
                        if (lastFactor != result)
                            AddModifier(calculation, state.Model, DamageContributionStage.Multiplicative,
                                state.InputValue * lastFactor, state.InputValue * result,
                                state.InputValue * (result - lastFactor), result / (lastFactor == 0m ? 1m : lastFactor),
                                state.MethodName);
                        calculation.CurrentValue = state.InputValue * result;
                        return;
                    }

                    output = state.InputValue * result;
                    contribution = output - state.InputValue;
                    factor = result;
                    break;
                case DamageContributionStage.HpLoss:
                    output = result;
                    contribution = output - state.InputValue;
                    break;
                default:
                    output = Math.Min(state.InputValue, result);
                    contribution = output - state.InputValue;
                    break;
            }

            if (contribution == 0m)
                return;
            calculation.CurrentValue = output;
            AddModifier(calculation, state.Model, stage, state.InputValue, output, contribution, factor,
                state.MethodName);
        }

        private static void AddModifier(
            DamageCalculationCapture calculation,
            AbstractModel model,
            DamageContributionStage stage,
            decimal input,
            decimal output,
            decimal contribution,
            decimal? factor,
            string context)
        {
            if (contribution == 0m)
                return;
            calculation.Modifiers.Add(new(
                GameDescriptorFactory.Model(model),
                stage,
                input,
                output,
                contribution,
                factor,
                model,
                context));
        }

        internal static bool TryConsume(
            Creature receiver,
            Creature? dealer,
            CardModel? cardSource,
            ValueProp props,
            out DamageRequestCapture? request,
            out DamageCalculationCapture? calculation)
        {
            request = CurrentRequestValue.Value;
            calculation = null;
            if (request == null)
                return false;
            lock (request.Calculations)
            {
                calculation = request.Calculations.FirstOrDefault(candidate =>
                    !candidate.Consumed && ReferenceEquals(candidate.Target, receiver) && candidate.Props == props);
                calculation ??= request.Calculations.FirstOrDefault(candidate =>
                    !candidate.Consumed && candidate.Props == props && ReferenceEquals(candidate.Dealer, dealer) &&
                    ReferenceEquals(candidate.CardSource, cardSource));
                calculation ??= request.Calculations.FirstOrDefault(candidate => !candidate.Consumed);
                if (calculation == null)
                    return false;
                calculation.Consumed = true;
                return true;
            }
        }

        private static ArgumentLayout Layout(MethodBase method)
        {
            return ArgumentLayouts.GetOrAdd(method, static value => new(value));
        }

        private static T? Argument<T>(object[] args, int index)
        {
            return index >= 0 && index < args.Length && args[index] is T value ? value : default;
        }

        private static Creature[] Targets(object[] args, ArgumentLayout layout)
        {
            if (Argument<Creature>(args, layout.TargetIndex) is { } creature)
                return [creature];
            if (Argument<IEnumerable<Creature>>(args, layout.TargetsIndex) is { } creatures)
                return creatures.ToArray();
            return [];
        }

        private sealed class ArgumentLayout
        {
            internal ArgumentLayout(MethodBase method)
            {
                MethodName = method.Name;
                var parameters = method.GetParameters();
                AmountIndex = Find(parameters, "amount");
                DamageIndex = Find(parameters, "damage");
                PropsIndex = Find(parameters, "props");
                DealerIndex = Find(parameters, "dealer");
                CardSourceIndex = Find(parameters, "cardSource");
                TargetIndex = Find(parameters, "target");
                TargetsIndex = Find(parameters, "targets");
            }

            internal string MethodName { get; }
            internal int AmountIndex { get; }
            internal int DamageIndex { get; }
            internal int PropsIndex { get; }
            internal int DealerIndex { get; }
            internal int CardSourceIndex { get; }
            internal int TargetIndex { get; }
            internal int TargetsIndex { get; }

            private static int Find(ParameterInfo[] parameters, string name)
            {
                for (var index = 0; index < parameters.Length; index++)
                    if (string.Equals(parameters[index].Name, name, StringComparison.OrdinalIgnoreCase))
                        return index;
                return -1;
            }
        }

        internal sealed record RequestState(DamageRequestCapture Request, DamageRequestCapture? Previous);

        internal sealed record CalculationState(
            DamageRequestCapture Request,
            DamageCalculationCapture Calculation,
            DamageCalculationCapture? Previous);

        internal sealed record ModifierState(
            DamageCalculationCapture Calculation,
            AbstractModel Model,
            string MethodName,
            decimal InputValue,
            int FactorModifierStartIndex);

        internal sealed record FactorModifierState(
            DamageCalculationCapture Calculation,
            AbstractModel Model,
            string MethodName,
            decimal InputFactor);

        internal sealed record HpLossState(
            DamageCalculationCapture Calculation,
            DamageCalculationCapture? Previous);
    }
}
