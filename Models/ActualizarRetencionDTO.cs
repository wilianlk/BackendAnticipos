namespace BackendAnticipos.Models
{
    public class ActualizarRetencionDTO
    {
        public int IdAnticipo { get; set; }
        public decimal? RetencionFuente { get; set; }
        public decimal? RetencionIva { get; set; }
        public decimal? RetencionIca { get; set; }
        public decimal? OtrasDeducciones { get; set; }
        public decimal? ValorAPagar { get; set; }
        public string? MotivoRechazo { get; set; }
        public string? DetalleMotivoRechazo { get; set; }
    }
}
