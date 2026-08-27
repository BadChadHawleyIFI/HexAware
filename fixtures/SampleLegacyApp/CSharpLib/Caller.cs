namespace CSharpLib
{
    // Cross-language call target: this method's caller lives in the VB.NET project.
    public class Caller
    {
        public void RunBilling()
        {
            var derived = new VbLib.DerivedClass();
            derived.CalculateTax();
        }
    }
}
