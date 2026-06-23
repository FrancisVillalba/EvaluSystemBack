namespace EvaluSystemBack.Dtos;

public record ArchivoUploadResponse(
    bool Guardado,
    string NombreOriginal,
    string NombreDescarga,
    string NombreGuardado,
    string Ruta,
    long TamanioBytes);

public record ArchivoBase64Response(
    string NombreArchivo,
    string ContentType,
    string Base64,
    long TamanioBytes);
