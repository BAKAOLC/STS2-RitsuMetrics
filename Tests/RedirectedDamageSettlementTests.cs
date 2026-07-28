// SPDX-License-Identifier: MPL-2.0

using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuMetrics.Capture;
using STS2RitsuMetrics.Core;

namespace STS2RitsuMetrics.Tests
{
    public sealed class RedirectedDamageSettlementTests
    {
        [Fact]
        public void RedirectedDamageSharesBlockAndHpLossSettlement()
        {
            var player = Creature();
            var osty = Creature();
            var calculation = Calculation(player, 16m,
                (player, 1m),
                (osty, 1m),
                (player, 0m));
            var request = Request(player, calculation);
            var ostyResult = Result(osty, 1);
            var playerResult = Result(player, blocked: 15);

            var group = Assert.Single(DamageCaptureHub.GroupResults(request, [ostyResult, playerResult]));

            Assert.Same(calculation, group.Calculation);
            Assert.Equal([ostyResult, playerResult], group.Results);
            var overkill = CombatAnalyticsService.ResolveTerminalOverkill(calculation, group.Results);
            var blocked = group.Results.Sum(static result => result.BlockedDamage);
            var hpLost = group.Results.Sum(static result => result.UnblockedDamage);
            Assert.Equal(15, blocked);
            Assert.Equal(1, hpLost);
            Assert.Equal(16, blocked + hpLost);
            Assert.Equal(0m, overkill);
        }

        [Fact]
        public void RedirectedIntermediateOverkillContinuesIntoOriginalTarget()
        {
            var player = Creature();
            var osty = Creature();
            var calculation = Calculation(player, 9m,
                (player, 9m),
                (osty, 9m),
                (player, 1m));
            var request = Request(player, calculation);
            var ostyResult = Result(osty, 8, overkill: 1);
            var playerResult = Result(player, 1);

            var group = Assert.Single(DamageCaptureHub.GroupResults(request, [ostyResult, playerResult]));
            var terminalOverkill = CombatAnalyticsService.ResolveTerminalOverkill(calculation, group.Results);
            var hpLost = group.Results.Sum(static result => result.UnblockedDamage);

            Assert.Equal(0m, terminalOverkill);
            Assert.Equal(9, hpLost);
            Assert.Equal(1, ostyResult.OverkillDamage);
        }

        [Fact]
        public void RedirectedResultsAreEmittedWhenTheHistoryPairIsComplete()
        {
            var previous = CaptureBridge.IsCombatActive;
            var player = Creature();
            var osty = Creature();
            var calculation = Calculation(player, 16m,
                (player, 1m),
                (osty, 1m),
                (player, 0m));
            var method = typeof(RedirectedDamageSettlementTests).GetMethod(
                nameof(CaptureRequest),
                BindingFlags.NonPublic | BindingFlags.Static)!;
            CaptureBridge.IsCombatActive = static () => true;
            var state = DamageCaptureHub.BeginRequest(method,
                [16m, ValueProp.Move, null!, new[] { player }]);
            Assert.NotNull(state);
            state.Request.Calculations.Add(calculation);
            try
            {
                Assert.True(DamageCaptureHub.ObserveResult(Result(osty, 1), out var first));
                Assert.Empty(first);

                Assert.True(DamageCaptureHub.ObserveResult(Result(player, blocked: 15), out var second));
                var group = Assert.Single(second);
                Assert.Same(calculation, group.Calculation);
                Assert.Equal(2, group.Results.Count);
            }
            finally
            {
                DamageCaptureHub.RestoreRequest(state);
                CaptureBridge.IsCombatActive = previous;
            }
        }

        private static Creature Creature()
        {
            return (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
        }

        private static DamageCalculationCapture Calculation(
            Creature target,
            decimal amount,
            params (Creature Target, decimal Amount)[] passes)
        {
            var calculation = new DamageCalculationCapture(target, null, null, amount, ValueProp.Move, null)
            {
                ModifiedAmount = amount,
            };
            foreach (var (passTarget, passAmount) in passes)
                calculation.HpLossPasses.Add(new(passTarget, passAmount, 0)
                {
                    OutputValue = passAmount,
                    ModifierEndIndex = 0,
                });
            return calculation;
        }

        private static DamageRequestCapture Request(
            Creature target,
            DamageCalculationCapture calculation)
        {
            var request = new DamageRequestCapture(
                calculation.RequestedAmount,
                ValueProp.Move,
                null,
                null,
                [target],
                null);
            request.Calculations.Add(calculation);
            return request;
        }

        private static DamageResult Result(
            Creature receiver,
            int hpLost = 0,
            int blocked = 0,
            int overkill = 0)
        {
            return new(receiver, ValueProp.Move)
            {
                UnblockedDamage = hpLost,
                BlockedDamage = blocked,
                OverkillDamage = overkill,
            };
        }

        private static void CaptureRequest(
            decimal amount,
            ValueProp props,
            Creature? dealer,
            IEnumerable<Creature> targets)
        {
        }
    }
}
