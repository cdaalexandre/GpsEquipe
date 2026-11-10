using System.Globalization;
using System.Text;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Locations;
using Android.OS;

namespace GpsEquipeApp;

// Novo: servico em primeiro plano. Segue rodando com a tela apagada e com o
// app fechado, ate o usuario tocar em Parar.
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeLocation)]
public class RastreioService : Service, ILocationListener
{
    public static bool Ativo;

    private const string Url = "https://gpsequipe-app-bad2.azurewebsites.net/api/recebercoordenadas";
    private const int IdNotificacao = 1001;
    private const string Canal = "rastreio";
    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    private LocationManager? gps;
    private PowerManager.WakeLock? trava;
    private string celular = "";
    private int enviando;
    private int enviados;
    private bool recusado;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        celular = Prefs().GetString("celular", "") ?? "";
        if (celular.Length < 12)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        CriarCanal();
        var n = Notificacao("Rastreamento ativo");
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q) { StartForeground(IdNotificacao, n, ForegroundService.TypeLocation); }
        else { StartForeground(IdNotificacao, n); }
        Ativo = true;

        if (gps == null)
        {
            // Novo: trava parcial de CPU, para o envio nao dormir com a tela apagada.
            var energia = (PowerManager)GetSystemService(PowerService)!;
            trava = energia.NewWakeLock(WakeLockFlags.Partial, "GpsEquipe:rastreio");
            trava!.Acquire();
            gps = (LocationManager)GetSystemService(LocationService)!;
            gps.RequestLocationUpdates(LocationManager.GpsProvider, 10000, 0, this);
        }
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        gps?.RemoveUpdates(this);
        gps = null;
        if (trava != null && trava.IsHeld) { trava.Release(); }
        Ativo = false;
        if (!recusado) { Gravar("Rastreamento parado.", false); }
        base.OnDestroy();
    }

    public void OnLocationChanged(Location location)
    {
        // Novo: um envio por vez. Se o anterior ainda esta no ar, pula este ponto.
        if (Interlocked.Exchange(ref enviando, 1) == 1) { return; }
        var corpo = "{\"Celular\":\"" + celular + "\",\"Latitude\":"
            + location.Latitude.ToString(CultureInfo.InvariantCulture) + ",\"Longitude\":"
            + location.Longitude.ToString(CultureInfo.InvariantCulture) + "}";

        Task.Run(async () =>
        {
            try
            {
                using var r = await Http.PostAsync(Url, new StringContent(corpo, Encoding.UTF8, "application/json"));
                if ((int)r.StatusCode == 403)
                {
                    recusado = true;
                    Gravar("Número não cadastrado. Fale com o administrador.", false);
                    StopSelf();
                    return;
                }
                if (r.IsSuccessStatusCode)
                {
                    enviados++;
                    Gravar("Envios: " + enviados + " | último às " + DateTime.Now.ToString("HH:mm:ss"), true);
                }
                else
                {
                    Gravar("Falha no envio, código " + (int)r.StatusCode + ". Tentando de novo.", true);
                }
            }
            catch
            {
                Gravar("Sem conexão. Tentando de novo.", true);
            }
            finally
            {
                Interlocked.Exchange(ref enviando, 0);
            }
        });
    }

    public void OnProviderDisabled(string provider) => Gravar("GPS desligado. Ligue a localização.", true);
    public void OnProviderEnabled(string provider) => Gravar("GPS ligado.", true);
    public void OnStatusChanged(string? provider, Availability status, Bundle? extras) { }

    private ISharedPreferences Prefs() => GetSharedPreferences("gpsequipe", FileCreationMode.Private)!;

    // Grava o status para a tela e, se pedido, atualiza o texto da notificacao.
    private void Gravar(string texto, bool notificar)
    {
        Prefs().Edit()!.PutString("status", texto)!.Apply();
        if (notificar && Ativo)
        {
            var nm = (NotificationManager)GetSystemService(NotificationService)!;
            nm.Notify(IdNotificacao, Notificacao(texto));
        }
    }

    private void CriarCanal()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) { return; }
        var nm = (NotificationManager)GetSystemService(NotificationService)!;
        nm.CreateNotificationChannel(new NotificationChannel(Canal, "Rastreamento", NotificationImportance.Low));
    }

    private Notification Notificacao(string texto)
    {
        var abrir = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable);
        var b = Build.VERSION.SdkInt >= BuildVersionCodes.O ? new Notification.Builder(this, Canal) : new Notification.Builder(this);
        return b.SetContentTitle("GpsEquipe")!
            .SetContentText(texto)!
            .SetSmallIcon(ApplicationInfo!.Icon)!
            .SetOngoing(true)!
            .SetContentIntent(abrir)!
            .Build()!;
    }
}