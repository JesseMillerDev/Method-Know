using System.Threading.Channels;

namespace Know.ApiService.Services;

public class BackgroundQueue
{
    private readonly Channel<int> _queue;

    public BackgroundQueue()
    {
        // Unbounded channel for simplicity, but in prod maybe bounded to avoid OOM if backing up too much
        _queue = Channel.CreateUnbounded<int>();
    }

    public async ValueTask EnqueueAsync(int articleId)
    {
        await _queue.Writer.WriteAsync(articleId);
    }

    public async ValueTask<int> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }
}
