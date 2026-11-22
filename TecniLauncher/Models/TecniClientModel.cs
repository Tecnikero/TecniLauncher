using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.ComponentModel;
using System.Runtime.CompilerServices;

public class TecniClientModel : INotifyPropertyChanged
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Version { get; set; }
    public string Loader { get; set; }
    public string MinecraftVersion { get; set; }
    public string LoaderVersion { get; set; }
    public string Description { get; set; }
    public string Icon { get; set; } = "⚡";
    public List<string> Mods { get; set; } = new List<string>();

    /// <summary>"pvp" (por defecto) o "evento". Los eventos se muestran como tarjetas en la pestaña Eventos.</summary>
    public string Categoria { get; set; } = "pvp";

    [JsonIgnore]
    public bool EsEvento =>
        string.Equals(Categoria?.Trim(), "evento", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Categoria?.Trim(), "eventos", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Nicknames que pueden ver este cliente (sin distinguir mayúsculas). Vacío o ausente = todo el mundo.
    /// Solo cuentan las cuentas verificadas (Microsoft premium o TecniStudio), salvo que PermitirOffline sea true.
    /// </summary>
    public List<string> Permitidos { get; set; }

    /// <summary>Si es true, también cuenta el nickname de las cuentas offline (cualquiera puede escribirlo).</summary>
    public bool PermitirOffline { get; set; }

    [JsonIgnore] public bool EsRestringido => Permitidos != null && Permitidos.Exists(n => !string.IsNullOrWhiteSpace(n));

    /// <summary>¿Puede verlo este jugador? nickname = el de la sesión actual; verificado = premium o TecniStudio.</summary>
    public bool PuedeVer(string nickname, bool verificado)
    {
        if (!EsRestringido) return true;
        if (string.IsNullOrWhiteSpace(nickname) || (!verificado && !PermitirOffline)) return false;
        string n = nickname.Trim();
        return Permitidos.Exists(p => string.Equals(p?.Trim(), n, StringComparison.OrdinalIgnoreCase));
    }

    public string ModpackUrl { get; set; }
    public string Sha256 { get; set; }

    private static readonly System.Text.RegularExpressions.Regex RxId =
        new(@"^[A-Za-z0-9][A-Za-z0-9_-]{0,47}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // El Id se usa como nombre de carpeta: sin puntos ni separadores para evitar salir de Instances
    public static bool EsIdValido(string? id) => id != null && RxId.IsMatch(id);

    private double _selectedRam = 4;
    [JsonIgnore]
    public double SelectedRam
    {
        get => _selectedRam;
        set { _selectedRam = value; OnPropertyChanged(); RamCambiada?.Invoke(this); }
    }

    [JsonIgnore] public Action<TecniClientModel> RamCambiada { get; set; }

    private string _versionInstalada = "";
    [JsonIgnore]
    public string VersionInstalada
    {
        get => _versionInstalada;
        set
        {
            _versionInstalada = value ?? "";
            OnPropertyChanged(); OnPropertyChanged(nameof(EstaInstalado));
            OnPropertyChanged(nameof(HayActualizacion)); OnPropertyChanged(nameof(TextoBoton));
            OnPropertyChanged(nameof(TextoEstado)); OnPropertyChanged(nameof(EstadoVisible));
        }
    }

    private bool _ocupado;
    [JsonIgnore]
    public bool Ocupado
    {
        get => _ocupado;
        set { _ocupado = value; OnPropertyChanged(); OnPropertyChanged(nameof(BotonHabilitado)); OnPropertyChanged(nameof(TextoBoton)); }
    }

    private string _textoOcupado = "";
    [JsonIgnore]
    public string TextoOcupado
    {
        get => _textoOcupado;
        set { _textoOcupado = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(TextoBoton)); }
    }

    [JsonIgnore] public bool EstaInstalado => !string.IsNullOrEmpty(VersionInstalada);
    [JsonIgnore] public bool HayActualizacion => EstaInstalado && VersionInstalada != Version;
    [JsonIgnore] public bool BotonHabilitado => !Ocupado;
    [JsonIgnore] public bool EstadoVisible => EstaInstalado;

    [JsonIgnore]
    public string TextoBoton
    {
        get
        {
            if (Ocupado && !string.IsNullOrEmpty(TextoOcupado)) return TextoOcupado;
            if (!EstaInstalado) return Txt("Txt_InstalarCliente");
            return HayActualizacion ? Txt("Txt_ActualizarCliente") : Txt("Txt_Jugar");
        }
    }

    [JsonIgnore]
    public string TextoEstado => HayActualizacion
        ? Txt("Txt_ActualizacionDisponible")
        : (EstaInstalado ? Txt("Txt_Instalado") : "");

    public void RefrescarTextos()
    {
        OnPropertyChanged(nameof(TextoBoton));
        OnPropertyChanged(nameof(TextoEstado));
    }

    private static string Txt(string clave)
        => System.Windows.Application.Current?.TryFindResource(clave) as string ?? clave;

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}