using OpenApiMcp.Services;

namespace OpenApiMcp.Cli.Tests;

/// <summary>
/// Shared read-only state for the CLI test suite: a ContractStore pre-loaded with an
/// inline petstore contract. Consumed via IClassFixture&lt;CliFixture&gt;.
/// </summary>
public sealed class CliFixture : IDisposable
{
    public const string ContractId = "petstore";

    /// <summary>Minimal OAS 3.0.3 petstore — inline so tests have no file-path dependency.</summary>
    public const string PetstoreYaml = """
        openapi: "3.0.3"
        info:
          title: Petstore
          version: "1.0.0"
        paths:
          /pets:
            get:
              operationId: listPets
              summary: List all pets
              tags: [pets]
              responses:
                "200":
                  description: A list of pets
                  content:
                    application/json:
                      schema:
                        $ref: "#/components/schemas/PetList"
          /pets/{petId}:
            get:
              operationId: getPet
              summary: Get a pet by ID
              tags: [pets]
              parameters:
                - name: petId
                  in: path
                  required: true
                  schema:
                    type: integer
              responses:
                "200":
                  description: A single pet
                  content:
                    application/json:
                      schema:
                        $ref: "#/components/schemas/Pet"
        components:
          schemas:
            Pet:
              type: object
              required: [id, name]
              properties:
                id:
                  type: integer
                name:
                  type: string
            PetList:
              type: array
              items:
                $ref: "#/components/schemas/Pet"
        """;

    public ContractStore Store { get; }

    public CliFixture()
    {
        Store = new ContractStore();
        Store.RegisterFromContent(ContractId, PetstoreYaml, "yaml");
    }

    public void Dispose() { }
}
