namespace BackendAnticipos.Models
{
    public class RegistrarLegalizacionDto
    {
        public int IdAnticipo { get; set; }
        public bool Legalizado { get; set; }
        public string QuienLegaliza { get; set; }
    }
}
