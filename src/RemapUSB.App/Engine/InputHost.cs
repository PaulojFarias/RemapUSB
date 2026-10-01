using System.Windows.Forms;
using RemapUSB.Infrastructure;
using RemapUSB.Input;

namespace RemapUSB.Engine;

/// <summary>
/// Thread só para a entrada: Raw Input, hook de teclado e o motor vivem aqui, com prioridade alta
/// e um loop de mensagens próprio. Assim o hook responde na hora mesmo quando a interface WPF está
/// ocupada (desenhando, carregando a lista de apps...). O Windows pula o hook que demora a responder,
/// e a tecla passa sem remapear.
/// Tudo que a interface pede ao motor passa por Post/Call, que executam nesta thread.
/// </summary>
internal sealed class InputHost : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private Control? _marshal;

    public RawInputSource Input { get; private set; } = null!;
    public Remapper Remapper { get; private set; } = null!;

    public InputHost()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "RemapUSB.Input", Priority = ThreadPriority.Highest };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    private void Run()
    {
        // Controle invisível só para receber chamadas vindas de outras threads.
        _marshal = new Control();
        _ = _marshal.Handle;

        Input = new RawInputSource();
        Remapper = new Remapper(Input);
        _ready.Set();

        System.Windows.Forms.Application.Run();

        Remapper.Dispose();
        Input.Dispose();
        _marshal.Dispose();
    }

    public void Post(Action action) => _marshal!.BeginInvoke(() =>
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Write("ERRO", $"thread de entrada: {ex}");
        }
    });

    public T Call<T>(Func<T> func) => (T)_marshal!.Invoke(func)!;

    public void Dispose()
    {
        _marshal?.BeginInvoke(System.Windows.Forms.Application.ExitThread);
        _thread.Join(2000);
    }
}
