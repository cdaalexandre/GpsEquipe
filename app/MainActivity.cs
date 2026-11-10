using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;

[assembly: UsesPermission("android.permission.INTERNET")]
[assembly: UsesPermission("android.permission.ACCESS_FINE_LOCATION")]
[assembly: UsesPermission("android.permission.ACCESS_COARSE_LOCATION")]
[assembly: UsesPermission("android.permission.ACCESS_BACKGROUND_LOCATION")]
[assembly: UsesPermission("android.permission.FOREGROUND_SERVICE")]
[assembly: UsesPermission("android.permission.FOREGROUND_SERVICE_LOCATION")]
[assembly: UsesPermission("android.permission.POST_NOTIFICATIONS")]
[assembly: UsesPermission("android.permission.WAKE_LOCK")]
[assembly: UsesPermission("android.permission.RECEIVE_BOOT_COMPLETED")]
[assembly: UsesPermission("android.permission.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS")]

namespace GpsEquipeApp;

[Activity(Label = "GpsEquipe", MainLauncher = true, LaunchMode = LaunchMode.SingleTop)]
public class MainActivity : Activity
{
    private EditText? campo;
    private TextView? rotulo, faixa, numero, detalhe;
    private Button? botao, bateria;
    private readonly Handler relogio = new Handler(Looper.MainLooper!);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var dens = Resources?.DisplayMetrics?.Density ?? 1f;
        int Px(int v) => (int)(v * dens);

        var rolagem = new ScrollView(this);
        var tela = new LinearLayout(this) { Orientation = Orientation.Vertical };
        tela.SetPadding(Px(20), Px(24), Px(20), Px(24));
        rolagem.AddView(tela);

        tela.AddView(new TextView(this) { Text = "GpsEquipe", TextSize = 26 });
        tela.AddView(new TextView(this) { Text = "Rastreamento de colaboradores em campo", TextSize = 14 });

        // Novo: faixa grande de estado, verde, laranja ou vermelha.
        faixa = new TextView(this) { TextSize = 22, Gravity = GravityFlags.Center };
        faixa.SetTextColor(Color.White);
        faixa.SetPadding(Px(12), Px(18), Px(12), Px(18));
        faixa.LayoutParameters = Margem(Px(20));
        tela.AddView(faixa);

        // Novo: o numero confirmado fica em destaque.
        numero = new TextView(this) { TextSize = 20, Gravity = GravityFlags.Center };
        numero.SetPadding(0, Px(12), 0, 0);
        tela.AddView(numero);

        rotulo = new TextView(this) { Text = "Seu celular, com código do país e DDD", TextSize = 14 };
        rotulo.SetPadding(0, Px(16), 0, Px(4));
        tela.AddView(rotulo);
        campo = new EditText(this) { InputType = InputTypes.ClassPhone, Hint = "+55 DDD número", TextSize = 20 };
        campo.Text = Prefs().GetString("celular", "");
        tela.AddView(campo);

        botao = new Button(this) { TextSize = 18 };
        botao.LayoutParameters = Margem(Px(12));
        botao.Click += (s, e) => Alternar();
        tela.AddView(botao);

        detalhe = new TextView(this) { TextSize = 15 };
        detalhe.SetPadding(0, Px(16), 0, 0);
        tela.AddView(detalhe);

        bateria = new Button(this) { Text = "Liberar bateria (recomendado)" };
        bateria.LayoutParameters = Margem(Px(12));
        bateria.Click += (s, e) => PedirBateria();
        tela.AddView(bateria);

        var aviso = new TextView(this)
        {
            TextSize = 13,
            Text = "Como seus dados são tratados. Depois que você toca em LIGAR, este app envia seu número de celular e sua localização a cada 10 segundos, inclusive com a tela apagada e depois de reiniciar o celular. Uma notificação fixa mostra que o envio está ligado. O envio só para quando você toca em DESLIGAR. Os pontos aparecem apenas no mapa da equipe, acessível só a quem tem a chave de gestor. Não há exclusão automática. Para pedir a exclusão dos seus registros, fale com o administrador."
        };
        aviso.SetPadding(0, Px(28), 0, 0);
        tela.AddView(aviso);

        SetContentView(rolagem);
    }

    private static LinearLayout.LayoutParams Margem(int topo)
    {
        var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        lp.TopMargin = topo;
        return lp;
    }

    protected override void OnResume()
    {
        base.OnResume();
        // Novo: se estava ligado e o servico nao esta rodando, religa.
        if (Prefs().GetBoolean("ligado", false) && !RastreioService.Ativo) { IniciarServico(); }
        Tique();
    }

    protected override void OnPause()
    {
        relogio.RemoveCallbacksAndMessages(null);
        base.OnPause();
    }

    private void Tique()
    {
        Atualizar();
        relogio.PostDelayed(Tique, 1000);
    }

    private ISharedPreferences Prefs() => GetSharedPreferences("gpsequipe", FileCreationMode.Private)!;

    private void Atualizar()
    {
        var p = Prefs();
        var ligado = p.GetBoolean("ligado", false);
        var cel = p.GetString("celular", "") ?? "";
        var ultimo = p.GetLong("ultimoEnvio", 0);
        var envios = p.GetInt("envios", 0);
        var resposta = p.GetString("resposta", "") ?? "";
        var lat = p.GetString("lat", "") ?? "";
        var lon = p.GetString("lon", "") ?? "";
        var seg = ultimo > 0 ? (Java.Lang.JavaSystem.CurrentTimeMillis() - ultimo) / 1000 : -1;

        if (!ligado) { faixa!.Text = "DESLIGADO"; faixa.SetBackgroundColor(Color.ParseColor("#9E2A2B")); }
        else if (seg >= 0 && seg <= 30) { faixa!.Text = "ENVIANDO"; faixa.SetBackgroundColor(Color.ParseColor("#1E7B34")); }
        else { faixa!.Text = "LIGADO, AGUARDANDO ENVIO"; faixa.SetBackgroundColor(Color.ParseColor("#B26A00")); }

        numero!.Text = cel.Length >= 12 ? "Número: " + RastreioService.Formatar(cel) : "Nenhum número informado";
        rotulo!.Visibility = ligado ? ViewStates.Gone : ViewStates.Visible;
        campo!.Visibility = ligado ? ViewStates.Gone : ViewStates.Visible;
        botao!.Text = ligado ? "DESLIGAR envio" : "LIGAR envio";

        var hora = seg < 0 ? "nenhum ainda" : DateTimeOffset.FromUnixTimeMilliseconds(ultimo).ToLocalTime().ToString("HH:mm:ss") + " (há " + seg + " s)";
        detalhe!.Text = "Envios aceitos: " + envios
            + "\nÚltimo envio: " + hora
            + "\nÚltima posição: " + (lat == "" ? "-" : lat + ", " + lon)
            + "\nResposta do servidor: " + (resposta == "" ? "-" : resposta)
            + "\n\nDiagnóstico: fonte " + (p.GetString("fonte", "-") ?? "-")
            + "\nCiclos sem posição: " + p.GetInt("semPosicao", 0)
            + "\nPosições descartadas (erro > 100 m): " + p.GetInt("descartadas", 0)
            + "\nInícios do serviço: " + p.GetInt("inicios", 0)
            + (p.GetLong("ultimoInicio", 0) > 0 ? ", último às " + DateTimeOffset.FromUnixTimeMilliseconds(p.GetLong("ultimoInicio", 0)).ToLocalTime().ToString("HH:mm:ss") : "");

        var energia = (PowerManager)GetSystemService(PowerService)!;
        bateria!.Visibility = energia.IsIgnoringBatteryOptimizations(PackageName) ? ViewStates.Gone : ViewStates.Visible;
    }

    private void Alternar()
    {
        var p = Prefs();
        if (p.GetBoolean("ligado", false))
        {
            // Novo: desligar pede confirmacao. E o unico jeito de parar o envio.
            new AlertDialog.Builder(this)
                .SetTitle("Desligar o envio?")!
                .SetMessage("A localização deixa de ser enviada até você tocar em LIGAR de novo.")!
                .SetPositiveButton("Desligar", (s, e) =>
                {
                    Prefs().Edit()!.PutBoolean("ligado", false)!.PutString("resposta", "Desligado pelo usuário.")!.Apply();
                    StopService(new Intent(this, typeof(RastreioService)));
                    Atualizar();
                })!
                .SetNegativeButton("Continuar enviando", (s, e) => { })!
                .Show();
            return;
        }

        var digitos = new string((campo!.Text ?? "").Where(char.IsDigit).ToArray());
        if (digitos.Length < 12)
        {
            Toast.MakeText(this, "Informe o número completo, com código do país e DDD.", ToastLength.Long)!.Show();
            return;
        }

        if (CheckSelfPermission(Android.Manifest.Permission.AccessFineLocation) != Permission.Granted)
        {
            var pedir = new List<string> { Android.Manifest.Permission.AccessFineLocation };
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu) { pedir.Add("android.permission.POST_NOTIFICATIONS"); }
            RequestPermissions(pedir.ToArray(), 1);
            Toast.MakeText(this, "Autorize a localização e toque em LIGAR de novo.", ToastLength.Long)!.Show();
            return;
        }
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q && !p.GetBoolean("pediuFundo", false)
            && CheckSelfPermission("android.permission.ACCESS_BACKGROUND_LOCATION") != Permission.Granted)
        {
            p.Edit()!.PutBoolean("pediuFundo", true)!.Apply();
            RequestPermissions(new[] { "android.permission.ACCESS_BACKGROUND_LOCATION" }, 2);
            Toast.MakeText(this, "Escolha 'Permitir o tempo todo' e toque em LIGAR de novo.", ToastLength.Long)!.Show();
            return;
        }

        // Novo: confirmacao explicita do numero antes de ligar.
        new AlertDialog.Builder(this)
            .SetTitle("Confirma que este é o seu número?")!
            .SetMessage(RastreioService.Formatar(digitos))!
            .SetPositiveButton("Sim, ligar envio", (s, e) =>
            {
                Prefs().Edit()!
                    .PutString("celular", digitos)!
                    .PutBoolean("ligado", true)!
                    .PutInt("envios", 0)!
                    .PutLong("ultimoEnvio", 0)!
                    .PutString("lat", "")!
                    .PutString("lon", "")!
                    .PutInt("semPosicao", 0)!
                    .PutInt("descartadas", 0)!
                    .PutInt("inicios", 0)!
                    .PutLong("ultimoInicio", 0)!
                    .PutString("resposta", "Iniciando...")!
                    .Apply();
                IniciarServico();
                Atualizar();
            })!
            .SetNegativeButton("Corrigir", (s, e) => { })!
            .Show();
    }

    private void IniciarServico()
    {
        var intent = new Intent(this, typeof(RastreioService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O) { StartForegroundService(intent); } else { StartService(intent); }
    }

    private void PedirBateria()
    {
        var i = new Intent("android.settings.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS");
        i.SetData(Android.Net.Uri.Parse("package:" + PackageName));
        StartActivity(i);
    }
}