namespace BackendAnticipos.Models
{
    public class Proveedor
    {
        public string Codigo { get; set; }
        public string Nombre { get; set; }     
        public string Telefono { get; set; }
        public string CodigoNombre => $"{Codigo} - {Nombre}";
    }
}
