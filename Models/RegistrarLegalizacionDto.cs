namespace BackendAnticipos.Models
{
    public class RegistrarLegalizacionDto
    {
        public int IdAnticipo { get; set; }
        public bool Legalizado { get; set; }
        public string QuienLegaliza { get; set; }
        public List<IFormFile>? SoportesLegalizacion { get; set; }
        public List<string>? MontosSoporte { get; set; }
        public decimal? SaldoAFavor { get; set; }
        public decimal? TotalSoportes { get; set; }
        public decimal? RetFuente { get; set; }
        public decimal? RetIva { get; set; }
        public decimal? RetIca { get; set; }
        public decimal? OtrasDeducciones { get; set; }
        public string? Estado { get; set; }
    }

}
