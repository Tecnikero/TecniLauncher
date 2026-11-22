using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CmlLib.Core;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.Forge.Versions;
using CmlLib.Core.Version;

namespace TecniLauncher
{
    /// <summary>
    /// Igual que CmlLib.Core.Installer.Forge.ForgeInstaller.Install (v1.1.1), pero sin abrir en el
    /// navegador la página de anuncios de Forge (adfoc.us) que la librería abre tras cada instalación
    /// y que no se puede desactivar con ninguna opción. Usa las mismas piezas públicas de la librería.
    /// </summary>
    public class ForgeInstallerSinAnuncio
    {
        private readonly MinecraftLauncher _launcher;
        private readonly ForgeInstaller _catalogo;
        private readonly ForgeInstallerVersionMapper _mapper = new();

        public ForgeInstallerSinAnuncio(MinecraftLauncher launcher)
        {
            _launcher = launcher;
            _catalogo = new ForgeInstaller(launcher); // solo para leer la lista de versiones
        }

        public async Task<string> Install(string mcVersion)
        {
            var versiones = await _catalogo.GetForgeVersions(mcVersion);
            var mejor = versiones.FirstOrDefault(v => v.IsRecommendedVersion)
                ?? versiones.FirstOrDefault(v => v.IsLatestVersion)
                ?? versiones.FirstOrDefault()
                ?? throw new InvalidOperationException("Cannot find any version");
            return await Install(mejor, new ForgeInstallOptions());
        }

        public async Task<string> Install(string mcVersion, string forgeVersion)
        {
            var versiones = await _catalogo.GetForgeVersions(mcVersion);
            var encontrada = versiones.FirstOrDefault(v => v.ForgeVersionName == forgeVersion)
                ?? throw new InvalidOperationException("Cannot find version name " + forgeVersion);
            return await Install(encontrada, new ForgeInstallOptions());
        }

        public async Task<string> Install(ForgeVersion forgeVersion, ForgeInstallOptions options)
        {
            var instalador = _mapper.CreateInstaller(forgeVersion);
            if (options.SkipIfAlreadyInstalled && await EstaInstalada(instalador.VersionName))
                return instalador.VersionName;

            var vanilla = await _launcher.GetVersionAsync(forgeVersion.MinecraftVersionName);
            await _launcher.InstallAsync(vanilla, options.FileProgress, options.ByteProgress);

            if (string.IsNullOrEmpty(options.JavaPath))
                options.JavaPath = RutaJava(vanilla);

            await instalador.Install(_launcher.MinecraftPath, _launcher.GameInstaller, options);
            await _launcher.GetAllVersionsAsync();
            return instalador.VersionName;
        }

        private async Task<bool> EstaInstalada(string nombreVersion)
        {
            try
            {
                await _launcher.GetVersionAsync(nombreVersion);
                return true;
            }
            catch (KeyNotFoundException)
            {
                return false;
            }
        }

        private string RutaJava(IVersion version)
        {
            var java = _launcher.GetJavaPath(version);
            if (string.IsNullOrEmpty(java) || !File.Exists(java))
                java = _launcher.GetDefaultJavaPath();
            if (string.IsNullOrEmpty(java) || !File.Exists(java))
                throw new InvalidOperationException("Cannot find any java binary. Set java binary path");
            return java;
        }
    }
}
