// SPDX-License-Identifier: MPL-2.0

using STS2RitsuMetrics.Data.Models;

namespace STS2RitsuMetrics.Tests
{
    public sealed class HistoryArchiveNotificationTests
    {
        [Fact]
        public async Task PendingLoadCompletionRaisesOneReactiveNotification()
        {
            var archive = new HistoryArchive();
            var pending = new TaskCompletionSource<HistoryArchive>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var notifications = 0;
            archive.SetLoadCompletionCallback(() =>
            {
                Interlocked.Increment(ref notifications);
                notified.TrySetResult();
            });
            archive.AttachPendingLoad(pending.Task);

            pending.SetResult(new());
            await notified.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(1, notifications);
        }
    }
}
