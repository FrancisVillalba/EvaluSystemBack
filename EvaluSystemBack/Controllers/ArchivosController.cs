using EvaluSystemBack.Dtos;
using EvaluSystemBack.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ArchivosController : ControllerBase
{
    private readonly IConfiguracionService _configuracionService;
    private readonly IWebHostEnvironment _environment;

    public ArchivosController(IConfiguracionService configuracionService, IWebHostEnvironment environment)
    {
        _configuracionService = configuracionService;
        _environment = environment;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult<ArchivoUploadResponse>> Upload(IFormFile archivo, [FromForm] string? carpeta = null)
    {
        if (archivo.Length == 0)
        {
            return BadRequest(new { message = "El archivo esta vacio." });
        }

        var basePath = await GetBasePathAsync();
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Path.Combine(_environment.ContentRootPath, "Archivos");
        }

        var safeFolder = SanitizePathSegment(carpeta);
        var targetFolder = string.IsNullOrWhiteSpace(safeFolder)
            ? basePath
            : Path.Combine(basePath, safeFolder);

        Directory.CreateDirectory(targetFolder);

        var extension = Path.GetExtension(archivo.FileName);
        var safeOriginalName = Path.GetFileNameWithoutExtension(archivo.FileName);
        var fileName = $"{DateTime.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(targetFolder, fileName);

        await using (var stream = System.IO.File.Create(filePath))
        {
            await archivo.CopyToAsync(stream);
        }

        return Ok(new ArchivoUploadResponse(
            true,
            $"{safeOriginalName}{extension}",
            $"{safeOriginalName}{extension}",
            fileName,
            filePath,
            archivo.Length));
    }

    [HttpGet("download")]
    public async Task<IActionResult> Download([FromQuery] string ruta, [FromQuery] string? nombreDescarga = null)
    {
        if (string.IsNullOrWhiteSpace(ruta))
        {
            return BadRequest(new { message = "La ruta del archivo es obligatoria." });
        }

        var basePath = await GetBasePathAsync();
        var safeFilePath = ResolveSafeFilePath(basePath, ruta);
        if (safeFilePath is null)
        {
            return BadRequest(new { message = "La ruta del archivo no es valida." });
        }

        if (!System.IO.File.Exists(safeFilePath))
        {
            return NotFound(new { message = "El archivo no existe." });
        }

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(safeFilePath, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        var downloadName = string.IsNullOrWhiteSpace(nombreDescarga)
            ? Path.GetFileName(safeFilePath)
            : Path.GetFileName(nombreDescarga);

        return PhysicalFile(safeFilePath, contentType, downloadName, enableRangeProcessing: true);
    }

    [HttpGet("base64")]
    public async Task<ActionResult<ArchivoBase64Response>> GetBase64([FromQuery] string ruta, [FromQuery] string? nombreDescarga = null)
    {
        if (string.IsNullOrWhiteSpace(ruta))
        {
            return BadRequest(new { message = "La ruta del archivo es obligatoria." });
        }

        var basePath = await GetBasePathAsync();
        var safeFilePath = ResolveSafeFilePath(basePath, ruta);
        if (safeFilePath is null)
        {
            return BadRequest(new { message = "La ruta del archivo no es valida." });
        }

        if (!System.IO.File.Exists(safeFilePath))
        {
            return NotFound(new { message = "El archivo no existe." });
        }

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(safeFilePath, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        var bytes = await System.IO.File.ReadAllBytesAsync(safeFilePath);
        var fileName = string.IsNullOrWhiteSpace(nombreDescarga)
            ? Path.GetFileName(safeFilePath)
            : Path.GetFileName(nombreDescarga);

        return Ok(new ArchivoBase64Response(
            fileName,
            contentType,
            Convert.ToBase64String(bytes),
            bytes.LongLength));
    }

    private static string? SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Trim().Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return cleaned.Replace("..", "_");
    }

    private async Task<string> GetBasePathAsync()
    {
        var basePath = await _configuracionService.ObtenerValorAsync("RUTA_DE_ARCHIVOS", 1)
            ?? await _configuracionService.ObtenerValorAsync("FileStoragePath", 1);
        return string.IsNullOrWhiteSpace(basePath)
            ? Path.Combine(_environment.ContentRootPath, "Archivos")
            : basePath;
    }

    private static string? ResolveSafeFilePath(string basePath, string requestedPath)
    {
        var fullBasePath = Path.GetFullPath(basePath);
        var fullRequestedPath = Path.GetFullPath(requestedPath);

        var normalizedBasePath = fullBasePath.EndsWith(Path.DirectorySeparatorChar)
            ? fullBasePath
            : fullBasePath + Path.DirectorySeparatorChar;

        return fullRequestedPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase)
            ? fullRequestedPath
            : null;
    }
}
