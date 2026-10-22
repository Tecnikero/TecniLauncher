using System.Windows.Media;

namespace TecniLauncher.Models
{
    public enum NivelLog { Info, Advertencia, Error, Sistema }
    public class LineaLog
    {
        private static readonly Brush PincelInfo = Congelar(0xC9, 0xD1, 0xD9);
        private static readonly Brush PincelAdvertencia = Congelar(0xF2, 0xCC, 0x60);
        private static readonly Brush PincelError = Congelar(0xFF, 0x6B, 0x6B);
        private static readonly Brush PincelSistema = Congelar(0x4F, 0xAC, 0xFE);

        public string Texto { get; }
        public NivelLog Nivel { get; }

        public Brush Pincel => Nivel switch
        {
            NivelLog.Advertencia => PincelAdvertencia,
            NivelLog.Error => PincelError,
            NivelLog.Sistema => PincelSistema,
            _ => PincelInfo
        };

        public LineaLog(string texto, NivelLog nivel)
        {
            Texto = texto;
            Nivel = nivel;
        }

        private static Brush Congelar(byte r, byte g, byte b)
        {
            var pincel = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
            pincel.Freeze();
            return pincel;
        }
    }
}
