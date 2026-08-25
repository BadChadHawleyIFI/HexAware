# SampleLegacyApp

A fixture solution for testing HexAware.

## Getting Started

Run `hex-generate` against `SampleLegacyApp.sln`.

### Prerequisites

.NET Framework 4.7.2 targeting pack.

## Architecture

See `VbLib` and `CSharpLib` for the cross-language call graph fixture. `DerivedClass.CalculateTax()` is the
main entry point for tax calculation logic.
