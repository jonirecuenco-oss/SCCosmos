using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using SCCosmos.Sincro;

namespace SCCosmos
{
    /// <summary>
    /// Canal por carpeta en Windows: los archivos de cambios viven en
    /// la subcarpeta «Sincro» de la carpeta compartida que el usuario
    /// ya sincroniza. Cada dispositivo escribe solo su propio archivo.
    /// </summary>
    public sealed class CanalArchivosPc
        : ICanalArchivosSincro
    {
        private readonly string _carpeta;


        public CanalArchivosPc(
            string carpeta)
        {
            _carpeta =
                carpeta;

            Directory.CreateDirectory(
                carpeta);
        }


        public Task<List<string>> ListarAsync(
            string prefijo)
        {
            var lista =
                new List<string>();

            try
            {
                foreach (string ruta in
                         Directory.GetFiles(
                             _carpeta))
                {
                    string nombre =
                        Path.GetFileName(
                            ruta);

                    if (nombre.StartsWith(
                            prefijo,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        lista.Add(
                            nombre);
                    }
                }
            }
            catch
            {
            }

            return Task.FromResult(
                lista);
        }


        public Task<Dictionary<string, string?>> ListarConFirmasAsync(
            string prefijo)
        {
            var firmas =
                new Dictionary<string, string?>(
                    StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (FileInfo archivo in
                         new DirectoryInfo(
                             _carpeta)
                             .EnumerateFiles())
                {
                    if (archivo.Name.StartsWith(
                            prefijo,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        firmas[archivo.Name] =
                            $"{archivo.Length}:{archivo.LastWriteTimeUtc.Ticks}";
                    }
                }
            }
            catch
            {
            }

            return Task.FromResult(
                firmas);
        }


        public Task<byte[]?> LeerAsync(
            string nombre)
        {
            try
            {
                string ruta =
                    Path.Combine(
                        _carpeta,
                        nombre);

                return Task.FromResult<byte[]?>(
                    File.Exists(
                        ruta)
                        ? File.ReadAllBytes(
                            ruta)
                        : null);
            }
            catch
            {
                return Task.FromResult<byte[]?>(
                    null);
            }
        }


        public Task<byte[]?> LeerDesdeAsync(
            string nombre,
            long desde)
        {
            try
            {
                string ruta =
                    Path.Combine(
                        _carpeta,
                        nombre);

                if (!File.Exists(
                        ruta))
                {
                    return Task.FromResult<byte[]?>(
                        null);
                }

                using var archivo =
                    new FileStream(
                        ruta,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);

                if (desde >= archivo.Length)
                {
                    return Task.FromResult<byte[]?>(
                        Array.Empty<byte>());
                }

                archivo.Seek(
                    desde,
                    SeekOrigin.Begin);

                var bytes =
                    new byte[archivo.Length - desde];

                archivo.ReadExactly(
                    bytes);

                return Task.FromResult<byte[]?>(
                    bytes);
            }
            catch
            {
                return Task.FromResult<byte[]?>(
                    null);
            }
        }


        public Task<long> TamanoAsync(
            string nombre)
        {
            try
            {
                var archivo =
                    new FileInfo(
                        Path.Combine(
                            _carpeta,
                            nombre));

                return Task.FromResult(
                    archivo.Exists
                        ? archivo.Length
                        : -1L);
            }
            catch
            {
                return Task.FromResult(
                    0L);
            }
        }


        public async Task<bool> EscribirAsync(
            string nombre,
            IReadOnlyList<string> lineas)
        {
            try
            {
                await File.WriteAllTextAsync(
                    Path.Combine(
                        _carpeta,
                        nombre),
                    string.Join(
                        "\n",
                        lineas)
                    + "\n");

                return true;
            }
            catch
            {
                return false;
            }
        }


        public async Task<bool> AgregarAsync(
            string nombre,
            IReadOnlyList<string> lineas)
        {
            try
            {
                await File.AppendAllTextAsync(
                    Path.Combine(
                        _carpeta,
                        nombre),
                    string.Join(
                        "\n",
                        lineas)
                    + "\n");

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
