using Android.App;
using Android.Content;
using Android.OS;

namespace GpsEquipeApp;

// Novo: depois de reiniciar o celular, religa o envio se ele estava ligado.
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted })]
public class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || intent?.Action != Intent.ActionBootCompleted) { return; }
        var p = context.GetSharedPreferences("gpsequipe", FileCreationMode.Private);
        if (p == null || !p.GetBoolean("ligado", false)) { return; }
        var i = new Intent(context, typeof(RastreioService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O) { context.StartForegroundService(i); } else { context.StartService(i); }
    }
}