using System.Data;
using System.Security.Claims;
using EvaluSystemBack.Data;
using EvaluSystemBack.Dtos;
using EvaluSystemBack.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EvaluSystemBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PagosVentasImpresionController : ControllerBase
{
    private const string EstadoPagoPendiente = "P1";
    private const string EstadoPagoParcial = "P2";
    private const string EstadoPagoPagado = "P3";

    private readonly EvaluSystemDbContext _context;

    public PagosVentasImpresionController(EvaluSystemDbContext context)
    {
        _context = context;
    }

    [HttpGet("venta/{ventaId:int}")]
    public async Task<ActionResult<IEnumerable<PagoVentaImpresionDto>>> GetByVenta(
        int ventaId,
        CancellationToken cancellationToken)
    {
        var pagos = await _context.PagosVentasImpresion
            .AsNoTracking()
            .Where(x => x.VentaImpresionId == ventaId)
            .OrderByDescending(x => x.FechaHora)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        return Ok(pagos.Select(ToDto));
    }
    [HttpPost]
    public async Task<ActionResult<PagoVentaImpresionDto>> Create(
        PagoVentaImpresionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var usuarioId))
        {
            return Unauthorized();
        }

        if (!await _context.FormasPago.AnyAsync(
                x => x.Id == request.FormaPagoId && x.Estado == true,
                cancellationToken))
        {
            return BadRequest(new { message = "La forma de pago no existe o está inactiva." });
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var venta = await _context.VentasImpresionCab
            .FirstOrDefaultAsync(x => x.Id == request.VentaImpresionId, cancellationToken);
        if (venta is null)
        {
            return NotFound(new { message = "La venta no existe." });
        }

        var pago = new PagoVentaImpresion
        {
            VentaImpresionId = request.VentaImpresionId,
            FechaHora = DateTime.Now,
            UsuarioId = usuarioId,
            FormaPagoId = request.FormaPagoId,
            Monto = request.Monto,
            RutaComprobante = request.RutaComprobante,
            NombreComprobante = request.NombreComprobante
        };

        _context.PagosVentasImpresion.Add(pago);
        await _context.SaveChangesAsync(cancellationToken);
        await RecalcularPagoVentaAsync(venta, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Created($"/api/PagosVentasImpresion/{pago.Id}", ToDto(pago));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var pago = await _context.PagosVentasImpresion
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (pago is null)
        {
            return NotFound();
        }

        var venta = await _context.VentasImpresionCab
            .FirstOrDefaultAsync(x => x.Id == pago.VentaImpresionId, cancellationToken);

        _context.PagosVentasImpresion.Remove(pago);
        await _context.SaveChangesAsync(cancellationToken);

        if (venta is not null)
        {
            await RecalcularPagoVentaAsync(venta, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private async Task RecalcularPagoVentaAsync(
        VentaImpresionCab venta,
        CancellationToken cancellationToken)
    {
        var montoPagado = await _context.PagosVentasImpresion
            .Where(x => x.VentaImpresionId == venta.Id)
            .SumAsync(x => x.Monto, cancellationToken);

        venta.MontoPagado = montoPagado;
        venta.EstadoPagadoId = montoPagado <= 0
            ? EstadoPagoPendiente
            : montoPagado < venta.TotalVenta
                ? EstadoPagoParcial
                : EstadoPagoPagado;
    }

    private bool TryGetCurrentUserId(out int usuarioId)
    {
        var value = User.FindFirstValue("usuarioId")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out usuarioId);
    }

    private static PagoVentaImpresionDto ToDto(PagoVentaImpresion pago)
    {
        return new PagoVentaImpresionDto(
            pago.Id,
            pago.VentaImpresionId,
            pago.FechaHora,
            pago.UsuarioId,
            pago.FormaPagoId,
            pago.Monto,
            pago.RutaComprobante,
            pago.NombreComprobante);
    }
}

