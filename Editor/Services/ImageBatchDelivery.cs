using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Tsukuyomi.AiCanvas.Editor
{
    internal static class ImageBatchDelivery
    {
        // Unity asset writes must run on the caller's editor thread, even when a provider
        // delivers a result from its transport thread. Awaiting delivery applies backpressure.
        internal static async Task GenerateAsync(IImageProvider provider, ImageRequest request,
            Action<int, byte[]> save, CancellationToken token)
        {
            var context = SynchronizationContext.Current;
            int thread = Thread.CurrentThread.ManagedThreadId;
            int received = 0;
            bool accepting = true;
            void Save(int index, byte[] png)
            {
                if (!accepting || index != received || index >= request.Count || png == null || png.Length == 0)
                    throw new InvalidOperationException("Provider must deliver each requested image once, in output order.");
                // An image already completed by the provider is retained even if cancellation
                // arrives now. Cancellation stops subsequent work, not persistence of paid results.
                save(index, png);
                received++;
            }
            Task Deliver(int index, byte[] png)
            {
                if (Thread.CurrentThread.ManagedThreadId == thread)
                {
                    Save(index, png);
                    return Task.CompletedTask;
                }
                if (context == null)
                    return Task.FromException(new InvalidOperationException("Image delivery requires an editor synchronization context."));
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                context.Post(_ =>
                {
                    try { Save(index, png); completion.TrySetResult(true); }
                    catch (Exception exception) { completion.TrySetException(exception); }
                }, null);
                return completion.Task;
            }
            try
            {
                token.ThrowIfCancellationRequested();
                if (provider is IIncrementalImageProvider incremental)
                    await incremental.GenerateAsync(request, Deliver, token);
                else
                {
                    var images = await provider.GenerateAsync(request, token);
                    if (images == null || images.Count != request.Count || images.Any(png => png == null || png.Length == 0))
                        throw new InvalidOperationException($"Provider must return exactly {request.Count} non-empty images.");
                    for (int index = 0; index < images.Count; index++) await Deliver(index, images[index]);
                }
                if (received != request.Count)
                    throw new InvalidOperationException($"Provider delivered {received} of {request.Count} requested images.");
            }
            finally { accepting = false; }
        }
    }
}
