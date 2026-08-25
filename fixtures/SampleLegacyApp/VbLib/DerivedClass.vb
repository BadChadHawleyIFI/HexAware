Public Class ButtonStub
    Public Event Click As EventHandler
End Class

' Fixture target: Inherits BaseClass, wires an event via Handles, and is called
' cross-language from CSharpLib.Caller.RunBilling().
Public Class DerivedClass
    Inherits BaseClass

    Public WithEvents SubmitButton As New ButtonStub

    Public Sub CalculateTax()
        LogMessage("Calculating tax")
    End Sub

    Public Sub SubmitButton_Click() Handles SubmitButton.Click
        CalculateTax()
    End Sub
End Class
