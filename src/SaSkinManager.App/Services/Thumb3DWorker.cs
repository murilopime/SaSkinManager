using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace SaSkinManager.App.Services;

/// <summary>
/// Gera as miniaturas 3D numa thread STA própria (o RenderTargetBitmap precisa de STA),
/// sem travar a interface. Os trabalhos mais recentes de "prioridade" passam na frente.
/// </summary>
public sealed class Thumb3DWorker : IDisposable
{
    public sealed record Job(string OutPath, Func<(byte[]? Dff, byte[]? Txd)> Load, Action<bool> Done);

    private readonly ConcurrentQueue<Job> _queue = new();
    private readonly ConcurrentStack<Job> _priority = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _cts = new();
    private readonly int _size;
    private readonly ManualResetEventSlim _started = new(false);

    /// <summary>
    /// Libera o trabalho. Renderizar 3D numa thread separada enquanto a janela principal ainda está
    /// sendo criada pode travar o WPF, então só começa depois que a janela já apareceu.
    /// </summary>
    public void Start() => _started.Set();

    public Thumb3DWorker(int size = 192)
    {
        _size = size;
        var t = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Thumb3D" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    public void Enqueue(Job job, bool priority = false)
    {
        if (priority) _priority.Push(job); else _queue.Enqueue(job);
        _signal.Release();
    }

    private void Run()
    {
        var token = _cts.Token;
        try { _started.Wait(token); }
        catch (OperationCanceledException) { return; }
        while (!token.IsCancellationRequested)
        {
            try { _signal.Wait(token); }
            catch (OperationCanceledException) { return; }
            if (!_priority.TryPop(out var job) && !_queue.TryDequeue(out job)) continue;
            bool ok = false;
            try
            {
                if (!File.Exists(job.OutPath))
                {
                    var (dff, txd) = job.Load();
                    var model = ModelScene.TryBuild(dff, txd);
                    if (model is not null)
                    {
                        ModelScene.RenderThumb(model, job.OutPath, _size);
                        ok = true;
                    }
                }
                else ok = true;
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or OutOfMemoryException) { }
            try { job.Done(ok); } catch (Exception) { }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
    }
}
