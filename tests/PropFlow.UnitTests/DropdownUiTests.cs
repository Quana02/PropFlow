namespace PropFlow.UnitTests;

public sealed class DropdownUiTests
{
    private static readonly string SolutionDirectory = FindSolutionDirectory();
    private static string ResidentManagementFile(string fileName) =>
        Path.Combine(SolutionDirectory, "src", "PropFlow.Web.Client", "Features", "Resident", "Management", "Pages", fileName);

    [Fact]
    public void WebClient_UsesSharedSelectInsteadOfNativeSelectFields()
    {
        var client = Path.Combine(SolutionDirectory, "src", "PropFlow.Web.Client");
        var razorFiles = Directory.EnumerateFiles(client, "*.razor", SearchOption.AllDirectories);

        foreach (var file in razorFiles)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("<select", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<InputSelect", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SharedSelect_DeclaresKeyboardAndListboxAccessibility()
    {
        var source = File.ReadAllText(Path.Combine(SolutionDirectory, "src", "PropFlow.Web.Client", "Shared", "Forms", "PropFlowSelect.razor"));
        var script = File.ReadAllText(Path.Combine(SolutionDirectory, "src", "PropFlow.Web.Client", "Shared", "Forms", "PropFlowSelect.razor.js"));

        Assert.Contains("aria-expanded", source);
        Assert.Contains("aria-controls", source);
        Assert.Contains("aria-activedescendant", source);
        Assert.Contains("role=\"listbox\"", source);
        Assert.Contains("role=\"option\"", source);
        Assert.Contains("ArrowDown", source);
        Assert.Contains("ArrowUp", source);
        Assert.Contains("Escape", source);
        Assert.DoesNotContain("addEventListener('pointerdown'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("queueMicrotask", script, StringComparison.Ordinal);
        Assert.Contains("setTimeout", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ResidentCreateModal_UsesScrollableBodyAndCustomDatePickers()
    {
        var source = File.ReadAllText(ResidentManagementFile("ResidentManagement.razor"));
        var styles = File.ReadAllText(ResidentManagementFile("ResidentManagement.razor.css"));

        Assert.Contains("class=\"resident-modal-body\"", source);
        Assert.Contains("class=\"resident-modal-form-shell\"", source);
        Assert.DoesNotContain("type=\"date\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, source.Split("<PropFlowDatePicker", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Số giấy tờ@if", source, StringComparison.Ordinal);
        Assert.Contains("CreateResidentRequest form=new()", source, StringComparison.Ordinal);
        Assert.Contains("creating=saving=false", source, StringComparison.Ordinal);
        Assert.DoesNotContain("@onclick=\"Close\" disabled=\"@saving\"", source, StringComparison.Ordinal);
        Assert.Contains("@onclick:stopPropagation=\"true\"", source, StringComparison.Ordinal);
        Assert.Contains("<button type=\"button\" class=\"resident-modal-close\" @onclick=\"Close\" aria-label=\"Đóng\">×</button>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("@onpointerdown=\"Close\"", source, StringComparison.Ordinal);
        Assert.Contains(".resident-modal-form-shell ::deep form", styles, StringComparison.Ordinal);
        Assert.Contains(".resident-modal-body{flex:1 1 auto;min-height:0;overflow-x:hidden;overflow-y:auto", styles, StringComparison.Ordinal);
        Assert.Contains(".resident-modal-header>button{border:0;background:none;color:#94a3b8;font-size:1.45rem;cursor:pointer}", styles, StringComparison.Ordinal);
        Assert.Contains(".resident-modal-close{display:inline-flex;align-items:center;justify-content:center;width:2.25rem;height:2.25rem", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void ResidentCreateModal_StylesBlazorInputChildrenAcrossCssIsolation()
    {
        var styles = File.ReadAllText(ResidentManagementFile("ResidentManagement.razor.css"));

        Assert.Contains(".resident-form-grid ::deep input,.resident-form-grid ::deep textarea", styles, StringComparison.Ordinal);
        Assert.Contains(".resident-form-grid ::deep textarea{width:100%", styles, StringComparison.Ordinal);
        Assert.Contains(".resident-form-full{grid-column:1/-1", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void ResidentPortalAndManagerModals_UseSeparateCssClasses()
    {
        var styles = File.ReadAllText(Path.Combine(
            SolutionDirectory,
            "src",
            "PropFlow.Web.Client",
            "wwwroot",
            "css",
            "resident-portal.css"));

        var portalHeader = File.ReadAllText(Path.Combine(
            SolutionDirectory,
            "src",
            "PropFlow.Web.Client",
            "Features",
            "Resident",
            "Portal",
            "Components",
            "ResidentHeader.razor"));
        var manager = File.ReadAllText(ResidentManagementFile("ResidentManagement.razor"));

        Assert.Contains(".resident-portal-modal {", styles, StringComparison.Ordinal);
        Assert.Contains(".resident-portal-modal-backdrop {", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("\n.resident-modal {", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("\n.resident-modal-backdrop {", styles, StringComparison.Ordinal);
        Assert.Contains("class=\"resident-portal-modal\"", portalHeader, StringComparison.Ordinal);
        Assert.Contains("class=\"resident-modal\"", manager, StringComparison.Ordinal);
    }

    [Fact]
    public void ResidentManagement_UsesFieldValidationAndFilterPopover()
    {
        var source = File.ReadAllText(ResidentManagementFile("ResidentManagement.razor"));

        Assert.Contains("Bộ lọc cư dân", source, StringComparison.Ordinal);
        Assert.Contains("ActiveFilterCount", source, StringComparison.Ordinal);
        Assert.Contains("IdentityTypeOptions", source, StringComparison.Ordinal);
        Assert.Contains("CCCD", source, StringComparison.Ordinal);
        Assert.Contains("CMND", source, StringComparison.Ordinal);
        Assert.Contains("ValidationMessage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<label>Loại giấy tờ<input", source, StringComparison.Ordinal);
        Assert.DoesNotContain(">Tìm kiếm</button>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectingIdentityType_DoesNotValidateAnUntouchedEmptyIdentityNumber()
    {
        var source = File.ReadAllText(ResidentManagementFile("ResidentManagement.razor"));

        Assert.Contains("ShouldRevalidateIdentityNumber", source, StringComparison.Ordinal);
        Assert.Contains("IsModified(identityNumberField)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Notify(nameof(form.IdentityType));Notify(nameof(form.IdentityNumber))", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ResidentRoleSelector_DisablesHouseholdHeadWhenApartmentAlreadyHasOne()
    {
        var source = File.ReadAllText(ResidentManagementFile("ResidentManagement.razor"));

        Assert.Contains("HasActiveHouseholdHead", source, StringComparison.Ordinal);
        Assert.Contains("new(\"HOUSEHOLD_HEAD\",HasActiveHouseholdHead?\"Chủ hộ (căn hộ đã có chủ hộ)\":\"Chủ hộ\",HasActiveHouseholdHead)", source, StringComparison.Ordinal);
        Assert.Contains("ReconcileHouseholdHeadAvailability", source, StringComparison.Ordinal);
        Assert.Contains("if(!IsResident||form.ApartmentUnitId==Guid.Empty)return", source, StringComparison.Ordinal);
        Assert.Contains("ValueChanged=\"HouseholdRoleChangedWithHeadCheck\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppShell_DoesNotTrapFixedDialogsBelowTheHeader()
    {
        var styles = File.ReadAllText(Path.Combine(SolutionDirectory, "src", "PropFlow.Web.Client", "Layout", "Shared", "AppShell.razor.css"));

        Assert.Contains(".app-ambient-canvas {", styles, StringComparison.Ordinal);
        Assert.Contains("z-index: -1;", styles, StringComparison.Ordinal);
        Assert.DoesNotContain(".app-shell-ambient .app-main", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedDatePicker_DoesNotBlockModalScrollAndSupportsEscape()
    {
        var source = File.ReadAllText(Path.Combine(SolutionDirectory, "src", "PropFlow.Web.Client", "Shared", "Forms", "PropFlowDatePicker.razor.js"));
        var styles = File.ReadAllText(Path.Combine(SolutionDirectory, "src", "PropFlow.Web.Client", "Shared", "Forms", "PropFlowDatePicker.razor.css"));

        Assert.DoesNotContain("stopWheel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("preventDefault()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("addEventListener('pointerdown'", source, StringComparison.Ordinal);
        Assert.Contains("addEventListener('click'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("queueMicrotask", source, StringComparison.Ordinal);
        Assert.Contains("setTimeout", source, StringComparison.Ordinal);
        Assert.Contains("Escape", source, StringComparison.Ordinal);
        Assert.Contains("activeRoot", source, StringComparison.Ordinal);
        Assert.Contains("--pf-date-width", source, StringComparison.Ordinal);
        Assert.Contains("button[aria-label=\"Đóng\"]", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("--pf-date-width", StringComparison.Ordinal) < source.IndexOf("calendar.offsetHeight", StringComparison.Ordinal),
            "Date picker width must be applied before its height is measured for placement.");
        Assert.DoesNotContain("aspect-ratio:1", styles, StringComparison.Ordinal);
        Assert.Contains("max-height:calc(100vh - 1rem)", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedDatePicker_AllowsDirectYearSelection()
    {
        var source = File.ReadAllText(Path.Combine(SolutionDirectory, "src", "PropFlow.Web.Client", "Shared", "Forms", "PropFlowDatePicker.razor"));

        Assert.Contains("ToggleYearPicker", source, StringComparison.Ordinal);
        Assert.Contains("pf-date__years", source, StringComparison.Ordinal);
        Assert.Contains("SelectYear", source, StringComparison.Ordinal);
        Assert.Contains("scrollSelectedYear", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedDatePicker_AllowsStrictDayMonthYearTextEntry()
    {
        var source = File.ReadAllText(Path.Combine(SolutionDirectory, "src", "PropFlow.Web.Client", "Shared", "Forms", "PropFlowDatePicker.razor"));

        Assert.Contains("<input type=\"text\"", source, StringComparison.Ordinal);
        Assert.Contains("@oninput=\"HandleInputAsync\"", source, StringComparison.Ordinal);
        Assert.Contains("@onblur=\"HandleBlurAsync\"", source, StringComparison.Ordinal);
        Assert.Contains("PropFlowDateText.InvalidMessage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<button type=\"button\" class=\"pf-date__trigger\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ResidentAndApartmentForms_BlockSavingInvalidTypedDates()
    {
        var resident = File.ReadAllText(ResidentManagementFile("ResidentManagement.razor"));
        var apartments = File.ReadAllText(Path.Combine(SolutionDirectory, "src", "PropFlow.Web.Client", "Features", "Apartment", "Pages", "ApartmentManagement.razor"));
        var apartmentDetail = File.ReadAllText(Path.Combine(SolutionDirectory, "src", "PropFlow.Web.Client", "Features", "Apartment", "Pages", "ApartmentDetail.razor"));

        Assert.Equal(3, resident.Split("ValidityChanged=", StringSplitOptions.None).Length - 1);
        Assert.Contains("!dateOfBirthValid||!identityIssuedDateValid||!identityExpiryDateValid", resident, StringComparison.Ordinal);
        Assert.Contains("ValidityChanged=\"v=>handoverDateValid=v\"", apartments, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@(saving||!handoverDateValid)\"", apartments, StringComparison.Ordinal);
        Assert.Contains("ValidityChanged=\"v=>handoverDateValid=v\"", apartmentDetail, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@(savingEdit||!handoverDateValid)\"", apartmentDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void ResidentGender_UsesSharedDropdownWithMaleAndFemaleOptions()
    {
        var source = File.ReadAllText(ResidentManagementFile("ResidentManagement.razor"));

        Assert.Contains("GenderOptions", source, StringComparison.Ordinal);
        Assert.Contains("new(\"Nam\",\"Nam\")", source, StringComparison.Ordinal);
        Assert.Contains("new(\"Nữ\",\"Nữ\")", source, StringComparison.Ordinal);
        Assert.Contains("AriaLabel=\"Giới tính\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<label>Giới tính<InputText", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnerAndResident_DoesNotValidateTheHiddenResidencyTypeState()
    {
        var source = File.ReadAllText(ResidentManagementFile("ResidentManagement.razor"));
        var rules = File.ReadAllText(Path.Combine(SolutionDirectory, "src", "PropFlow.Web.Client", "Features", "Resident", "Management", "Models", "ResidentRelationshipFormRules.cs"));

        Assert.Contains("ResidentRelationshipFormRules.Build", source, StringComparison.Ordinal);
        Assert.Contains("var residencyType = ownerAndResident ? \"OWNER_OCCUPIED\" : state.ResidencyType", rules, StringComparison.Ordinal);
        Assert.Contains("HouseholdHeadResidencyId = member && Guid.TryParse", rules, StringComparison.Ordinal);
        Assert.Contains("RelationshipToHead = member ? state.RelationshipToHead : null", rules, StringComparison.Ordinal);
        Assert.Contains("ClearResidencyValidation", source, StringComparison.Ordinal);
        Assert.Contains("<ValidationMessage For=\"()=>residencyType\" />", source, StringComparison.Ordinal);
        Assert.Contains("<ValidationMessage For=\"()=>householdRole\" />", source, StringComparison.Ordinal);
        Assert.Equal(1, source.Split("Residents.CreateAsync(form)", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void SuccessfulResidentCreation_ReturnsToResidentDirectory()
    {
        var source = File.ReadAllText(ResidentManagementFile("ResidentManagement.razor"));

        Assert.Contains(
            "if(r.IsSuccess&&r.Data is not null){Close();await LoadAsync();return;}",
            source,
            StringComparison.Ordinal);
    }

    private static string FindSolutionDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "PropFlow.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Không tìm thấy PropFlow.sln từ thư mục kiểm thử.");
    }
}
