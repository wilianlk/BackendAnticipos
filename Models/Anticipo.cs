namespace BackendAnticipos.Models
{
    public class Anticipo
    {
        public int Id { get; set; }
        public string Proveedor { get; set; }
        public string Nit { get; set; }
        public DateTime FechaCreacion { get; set; }
        public string Concepto { get; set; }
        public decimal ValorAnticipo { get; set; }
        public decimal ValorPagado { get; set; }
        public bool Legalizado { get; set; }
    }
}
