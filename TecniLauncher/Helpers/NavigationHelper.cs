using System.Windows;

namespace TecniLauncher.Helpers
{
    public static class NavigationHelper
    {
        public static void Navegar(UIElement vistaActiva, params UIElement[] todasLasVistas)
        {
            foreach (var vista in todasLasVistas)
                vista.Visibility = Visibility.Collapsed;

            vistaActiva.Visibility = Visibility.Visible;
        }
    }
}

