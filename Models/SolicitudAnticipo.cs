using System;
using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations.Schema;

namespace BackendAnticipos.Models
{
    public class SolicitudAnticipo
    {
        public int IdAnticipo { get; set; }
        public int IdSolicitante { get; set; }
        public string Solicitante { get; set; }
        public int AprobadorId { get; set; }
        public string CorreoAprobador { get; set; }
        public string Proveedor { get; set; }
        public string NitProveedor { get; set; }
        public string Concepto { get; set; }
        public decimal ValorAnticipo { get; set; }
        public decimal ValorAPagar { get; set; }         
        public decimal? Pagado { get; set; }
        public string? SoporteNombre { get; set; }
        public string? SoportePagoNombre { get; set; }
        public DateTime FechaSolicitud { get; set; }
        public DateTime? FechaAprobacion { get; set; }
        public string? Estado { get; set; }
        public string? ApropVP { get; set; }
        public string? VP { get; set; }
        public string? TieneLegalizacion { get; set; }
        public string? QuienLegaliza { get; set; }
        public decimal? RetencionFuente { get; set; }
        public decimal? RetencionIva { get; set; }
        public decimal? RetencionIca { get; set; }
        public string? MotivoRechazo { get; set; }

        [NotMapped]                
        public IFormFile? Soporte { get; set; }
    }
}
