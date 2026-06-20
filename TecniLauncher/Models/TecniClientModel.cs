using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.ComponentModel;
using System.Runtime.CompilerServices;

public class TecniClientModel : INotifyPropertyChanged
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Version { get; set; }
    public string MinecraftVersion { get; set; }
    public string LoaderVersion { get; set; }
    public string Description { get; set; }
    public string Icon { get; set; } = "⚡";
    public List<string> Mods { get; set; } = new List<string>();

    public string ModpackUrl { get; set; }

    private double _selectedRam = 4;
    [JsonIgnore]
    public double SelectedRam
    {
        get => _selectedRam;
        set { _selectedRam = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}