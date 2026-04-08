using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace TecniLauncher
{
    public partial class VentanaMensaje : Window
    {
        public MessageBoxResult ResultadoUsuario { get; private set; } = MessageBoxResult.None;
        public VentanaMensaje(string mensaje, string titulo, MessageBoxButton botones)
        {
            InitializeComponent();
            txtTitulo.Text = titulo.ToUpper();
            txtMensaje.Text = mensaje;

            if (botones == MessageBoxButton.YesNo)
            {
                btnAceptar.Content = "SÍ";
                btnCancelar.Visibility = Visibility.Visible; 
                btnCancelar.Content = "NO";
            }
            else 
            {
                btnAceptar.Content = "ENTENDIDO";
                btnCancelar.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnAceptar_Click(object sender, RoutedEventArgs e)
        {
            ResultadoUsuario = MessageBoxResult.Yes;
            this.Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            ResultadoUsuario = MessageBoxResult.No;
            this.Close();
        }
        public static MessageBoxResult Mostrar(string mensaje, string titulo = "AVISO", MessageBoxButton botones = MessageBoxButton.OK)
        {
            var ventana = new VentanaMensaje(mensaje, titulo, botones);
            ventana.ShowDialog();
            return ventana.ResultadoUsuario;
        }
    }
}