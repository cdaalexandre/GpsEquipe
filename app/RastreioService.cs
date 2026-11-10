using System.Globalization;
using System.Text;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Locations;
using Android.OS;
using Java.Lang;
using Gms = Android.Gms.Location;

namespace GpsEquipeApp;

// Servico em primeiro plano. So para quando o usuario toca em DESLIGAR.
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeLocation)]
public class RastreioService : Service, ILocationListener
{
    public static bool Ativo;

    private const string Url = "https://gpsequipe-app-bad2.azurewebsites.net/api/recebercoordenadas";
    private const int IdNotificacao = 1001;
    private const string Canal = "rastreio";
    private const float PrecisaoMaxima = 100f;
    private static readonly HttpClient Http = new HttpClient { Timeout = System.TimeSpan.FromSeconds(15) };

    private readonly Handler ciclo = new Handler(Looper.MainLooper!);
    private LocationManager? gps;
    private Retorno? retorno;
    private PowerManager.WakeLock? trava;
    private Location? ultima;
    private string celular = "";
    private int enviando;
    private bool iniciado;

    // Novo: recebe as posicoes do Fused Location do Google Play Services.
    private class Retorno : Gms.LocationCallback
    {
        private readonly RastreioService dono;
        public Retorno(RastreioService dono) { this.dono = dono; }
        public override void OnLocationResult(Gms.LocationResult result) => dono.Considerar(result.LastLocation);
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var p = Prefs();
        celular = p.GetString("celular", "") ?? "";
        if (!p.GetBoolean("ligado", false) || celular.Length < 12)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        CriarCanal();
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q) { StartForeground(IdNotificacao, Notificacao(), ForegroundService.TypeLocation); }
        else { StartForeground(IdNotificacao, Notificacao()); }
        Ativo = true;

        if (!iniciado)
        {
            iniciado = true;
            // Novo: diagnostico. Cada inicio do servico fica contado, com horario.
            p.Edit()!.PutInt("inicios", p.GetInt("inicios", 0) + 1)!.PutLong("ultimoInicio", JavaSystem.CurrentTimeMillis())!.Apply();

            var energia = (PowerManager)GetSystemService(PowerService)!;
            trava = energia.NewWakeLock(WakeLockFlags.Partial, "GpsEquipe:rastreio");
            trava!.Acquire();
            IniciarPosicao();
            ciclo.PostDelayed(Ciclo, 3000);
        }
        return StartCommandResult.Sticky;
    }

    private void IniciarPosicao()
    {
        try
        {
            var bld = new Gms.LocationRequest.Builder(Gms.Priority.PriorityHighAccuracy, 10000);
            bld.SetMinUpdateIntervalMillis(5000);
            retorno = new Retorno(this);
            Gms.LocationServices.GetFusedLocationProviderClient(this).RequestLocationUpdates(bld.Build(), retorno, Looper.MainLooper!);
            Prefs().Edit()!.PutString("fonte", "fused")!.Apply();
            return;
        }
        catch (System.Exception)
        {
            retorno = null;
        }

        // Plano B: GPS e rede pelo LocationManager, como na versao 2.
        try
        {
            gps = (LocationManager)GetSystemService(LocationService)!;
            foreach (var prov in new[] { LocationManager.GpsProvider, LocationManager.NetworkProvider })
            {
                if (gps.IsProviderEnabled(prov))
                {
                    gps.RequestLocationUpdates(prov, 10000, 0, this);
                    Considerar(gps.GetLastKnownLocation(prov));
                }
            }
            Prefs().Edit()!.PutString("fonte", "gps+rede")!.Apply();
        }
        catch (SecurityException)
        {
            Resposta("Sem permissão de localização. Abra o app e toque em LIGAR.");
        }
    }

    public override void OnTaskRemoved(Intent? rootIntent)
    {
        if (Prefs().GetBoolean("ligado", false)) { Religar(); }
        base.OnTaskRemoved(rootIntent);
    }

    public override void OnDestroy()
    {
        ciclo.RemoveCallbacksAndMessages(null);
        if (retorno != null)
        {
            try { Gms.LocationServices.GetFusedLocationProviderClient(this).RemoveLocationUpdates(retorno); } catch (System.Exception) { }
            retorno = null;
        }
        gps?.RemoveUpdates(this);
        gps = null;
        if (trava != null && trava.IsHeld) { trava.Release(); }
        Ativo = false;
        iniciado = false;
        if (Prefs().GetBoolean("ligado", false)) { Religar(); }
        base.OnDestroy();
    }

    private void Religar()
    {
        try
        {
            var i = new Intent(ApplicationContext, typeof(RastreioService));
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O) { ApplicationContext!.StartForegroundService(i); } else { ApplicationContext!.StartService(i); }
        }
        catch (System.Exception) { }
    }

    private void Considerar(Location? l)
    {
        if (l == null) { return; }
        // Novo: posicao com erro grande "pula" no mapa e infla a distancia.
        if (l.HasAccuracy && l.Accuracy > PrecisaoMaxima)
        {
            var p = Prefs();
            p.Edit()!.PutInt("descartadas", p.GetInt("descartadas", 0) + 1)!.Apply();
            return;
        }
        if (ultima == null || l.Time >= ultima.Time) { ultima = l; }
    }

    public void OnLocationChanged(Location location) => Considerar(location);
    public void OnProviderDisabled(string provider) => Resposta("Localização desligada no celular. Ligue a localização.");
    public void OnProviderEnabled(string provider) { }
    public void OnStatusChanged(string? provider, Availability status, Bundle? extras) { }

    private void Ciclo()
    {
        if (!Ativo) { return; }
        Enviar();
        ciclo.PostDelayed(Ciclo, 10000);
    }

    private void Enviar()
    {
        var l = ultima;
        if (l == null || JavaSystem.CurrentTimeMillis() - l.Time > 60000)
        {
            var p = Prefs();
            p.Edit()!.PutInt("semPosicao", p.GetInt("semPosicao", 0) + 1)!.Apply();
            Resposta("Aguardando sinal do GPS. Vá para perto de uma janela ou área aberta.");
            return;
        }
        if (System.Threading.Interlocked.Exchange(ref enviando, 1) == 1) { return; }

        var lat = l.Latitude.ToString(CultureInfo.InvariantCulture);
        var lon = l.Longitude.ToString(CultureInfo.InvariantCulture);
        var corpo = "{\"Celular\":\"" + celular + "\",\"Latitude\":" + lat + ",\"Longitude\":" + lon + "}";

        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                using var r = await Http.PostAsync(Url, new StringContent(corpo, Encoding.UTF8, "application/json"));
                var codigo = (int)r.StatusCode;
                if (r.IsSuccessStatusCode)
                {
                    var p = Prefs();
                    p.Edit()!
                        .PutInt("envios", p.GetInt("envios", 0) + 1)!
                        .PutLong("ultimoEnvio", JavaSystem.CurrentTimeMillis())!
                        .PutString("lat", l.Latitude.ToString("F5", CultureInfo.InvariantCulture))!
                        .PutString("lon", l.Longitude.ToString("F5", CultureInfo.InvariantCulture))!
                        .PutString("resposta", codigo + ", coordenada recebida.")!
                        .Apply();
                    Notificar();
                }
                else if (codigo == 403)
                {
                    Resposta("403, número não cadastrado. Fale com o administrador.");
                }
                else
                {
                    Resposta(codigo + ", falha no servidor. Tentando de novo.");
                }
            }
            catch (System.Exception)
            {
                Resposta("Sem conexão com a internet. Tentando de novo.");
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref enviando, 0);
            }
        });
    }

    private ISharedPreferences Prefs() => GetSharedPreferences("gpsequipe", FileCreationMode.Private)!;

    private void Resposta(string texto)
    {
        Prefs().Edit()!.PutString("resposta", texto)!.Apply();
        Notificar();
    }

    private void Notificar()
    {
        if (!Ativo) { return; }
        var nm = (NotificationManager)GetSystemService(NotificationService)!;
        nm.Notify(IdNotificacao, Notificacao());
    }

    private void CriarCanal()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) { return; }
        var nm = (NotificationManager)GetSystemService(NotificationService)!;
        nm.CreateNotificationChannel(new NotificationChannel(Canal, "Rastreamento", NotificationImportance.Low));
    }

    private Notification Notificacao()
    {
        var p = Prefs();
        var envios = p.GetInt("envios", 0);
        var ultimo = p.GetLong("ultimoEnvio", 0);
        var resposta = p.GetString("resposta", "") ?? "";
        var linha = Formatar(celular) + " | " + envios + " envios"
            + (ultimo > 0 ? " | último " + System.DateTimeOffset.FromUnixTimeMilliseconds(ultimo).ToLocalTime().ToString("HH:mm:ss") : "");
        var abrir = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable);
        var b = Build.VERSION.SdkInt >= BuildVersionCodes.O ? new Notification.Builder(this, Canal) : new Notification.Builder(this);
        return b.SetContentTitle("GpsEquipe ligado, enviando localização")!
            .SetContentText(linha)!
            .SetStyle(new Notification.BigTextStyle().BigText(linha + "\n" + resposta))!
            .SetSmallIcon(ApplicationInfo!.Icon)!
            .SetOngoing(true)!
            .SetContentIntent(abrir)!
            .Build()!;
    }

    public static string Formatar(string d)
    {
        if (d.Length == 13) { return "+" + d.Substring(0, 2) + " (" + d.Substring(2, 2) + ") " + d.Substring(4, 5) + "-" + d.Substring(9); }
        if (d.Length == 12) { return "+" + d.Substring(0, 2) + " (" + d.Substring(2, 2) + ") " + d.Substring(4, 4) + "-" + d.Substring(8); }
        return "+" + d;
    }
}