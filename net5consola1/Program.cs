using System;

namespace net5Consola1
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            //Console.ReadLine();

            var ListaNombres = new[] { "Nombre1", "Nombre2", "Nombre3" };

            foreach (var item in ListaNombres)
            {
                Console.WriteLine( $"Esta esta es una prueba para {item}" );
            }
        }
    }
}
