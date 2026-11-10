using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Text;
using Android.Widget;

// Novo: permissoes declaradas por atributo. O build as escreve no manifesto.
[assembly: UsesPermission("android.permission.INTERNET")]
[assembly: UsesPermission("android.permission.ACCESS_FINE_LOCATION")]
[assembly: UsesPermission("android.permission.ACCESS_COARSE_LOCATION")]
[assembly: UsesPermission("android.permission.FOREGROUND_SERVICE")]
[assembly: UsesPermission("android.permission.FOREGROUND_SERVICE_LOCATION")]
[assembly: UsesPermission("android.permission.POST_NOTIFICATIONS")]
[assembly: UsesPermission("android.permission.WAKE_LOCK")]

namespace GpsEquipeApp;

[Activity(Label = "GpsEquipe", MainLauncher = true)]
public class MainActivity : Activity
{
    private EditText? campo;
    private Button? botao;
    private TextView? status;
    private readonly Handler relogio = new Handler(Looper.MainLooper!);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var densidade = Resources?.DisplayMetrics?.Density ?? 1f;
        int Px(int v) => (int)(v * densidade);

        // Novo: tela montada em codigo, sem arquivo de layout XML.
        var tela = new LinearLayout(this) { Orientation = Orientation.Vertical };
        tela.SetPadding(Px(24), Px(32), Px(24), Px(24));

        var titulo = new TextView(this) { Text = "GpsEquipe", TextSize = 24 };
        var rotulo = new TextView(this) { Text = "Seu celular, com código do país e DDD" };
        rotulo.SetPadding(0, Px(16), 0, Px(4));
        campo = new EditText(this) { InputType = InputTypes.ClassPhone, Hint = "+55 DDD numero" };
        campo.Text = Preferencias().GetString("celular", "");
        botao = new Button(this);
        botao.Click += (s, e) => Alternar();
        status = new TextView(this);
        status.SetPadding(0, Px(16), 0, 0);
        var aviso = new TextView(this)
        {
            TextSize = 13,
            Text = "Como seus dados são tratados. Enquanto o rastreamento estiver ativo, este app envia seu número de celular e sua localização a cada 10 segundos, inclusive com a tela apagada. Uma notificação fica visível o tempo todo. O envio só para quando você toca em Parar. Os pontos aparecem apenas no mapa da equipe, acessível só a quem tem a chave de gestor. Não há exclusão automática. Para pedir a exclusão dos seus registros, fale com o administrador."
        };
        aviso.SetPadding(0, Px(32), 0, 0);

        tela.AddView(titulo);
        tela.AddView(rotulo);
        tela.AddView(campo);
        tela.AddView(botao);
        tela.AddView(status);
        tela.AddView(aviso);
        SetContentView(tela);
    }

    protected override void OnResume()
    {
        base.OnResume();
        Tique();
    }

    protected override void OnPause()
    {
        relogio.RemoveCallbacksAndMessages(null);
        base.OnPause();
    }

    // Novo: a tela relê o status que o servico grava, a cada 2 segundos.
    private void Tique()
    {
        Atualizar();
        relogio.PostDelayed(Tique, 2000);
    }

    private ISharedPreferences Preferencias() => GetSharedPreferences("gpsequipe", FileCreationMode.Private)!;

    private void Atualizar()
    {
        var ativo = RastreioService.Ativo;
        botao!.Text = ativo ? "Parar rastreamento" : "Iniciar rastreamento";
        campo!.Enabled = !ativo;
        status!.Text = Preferencias().GetString("status", "") ?? "";
    }

    private void Alternar()
    {
        if (RastreioService.Ativo)
        {
            StopService(new Intent(this, typeof(RastreioService)));
            Atualizar();
            return;
        }

        var digitos = new string((campo!.Text ?? "").Where(char.IsDigit).ToArray());
        if (digitos.Length < 12)
        {
            status!.Text = "Informe o número completo, com código do país e DDD.";
            return;
        }

        // Novo: o Android exige pedir a permissao de localizacao em tempo de execucao.
        var faltam = new List<string>();
        if (CheckSelfPermission(Android.Manifest.Permission.AccessFineLocation) != Permission.Granted)
        {
            faltam.Add(Android.Manifest.Permission.AccessFineLocation);
        }
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu && CheckSelfPermission("android.permission.POST_NOTIFICATIONS") != Permission.Granted)
        {
            faltam.Add("android.permission.POST_NOTIFICATIONS");
        }
        if (faltam.Count > 0)
        {
            RequestPermissions(faltam.ToArray(), 1);
            status!.Text = "Autorize e toque em Iniciar de novo.";
            return;
        }

        Preferencias().Edit()!.PutString("celular", digitos)!.PutString("status", "Iniciando...")!.Apply();
        var intent = new Intent(this, typeof(RastreioService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O) { StartForegroundService(intent); } else { StartService(intent); }
        RastreioService.Ativo = true;
        Atualizar();
    }
}