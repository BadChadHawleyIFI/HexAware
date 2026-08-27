<%@ Page Language="VB" AutoEventWireup="false" CodeBehind="BillingPage.aspx.vb" Inherits="VbLib.BillingPage" %>
<html>
<body>
    <form id="form1" runat="server">
        <asp:Button id="btnSubmit" runat="server" OnClick="btnSubmit_Click" onclick="confirmSubmit()" Text="Submit" />
        <asp:Label id="lblStatus" runat="server"></asp:Label>
    </form>
    <script type="text/javascript">
        function confirmSubmit() {
            __doPostBack('btnSubmit', '');
            PageMethods.GetTaxAjax();
        }
    </script>
</body>
</html>
