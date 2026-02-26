using OpenApiMcp.Services;

namespace OpenApiMcp.IntegrationTests;

/// <summary>
/// Shared state for the test suite: a ContractStore loaded with the petstore sample
/// and a fresh SessionManager.  Consumed via IClassFixture&lt;PetstoreFixture&gt;.
/// </summary>
public sealed class PetstoreFixture : IDisposable
{
    public const string ContractId = "petstore-sample-api";

    // Inline petstore to keep tests self-contained and independent of file paths.
    public const string PetstoreYaml = """
        openapi: "3.0.3"
        info:
          title: Petstore Sample API
          version: "1.0.0"
          description: A sample Petstore API for integration tests.
        servers:
          - url: https://petstore.example.com/v1
            description: Production
        tags:
          - name: pets
            description: Everything about pets
        paths:
          /pets:
            get:
              operationId: listPets
              summary: List all pets
              tags: [pets]
              parameters:
                - name: limit
                  in: query
                  required: false
                  schema:
                    type: integer
                    maximum: 100
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
                    type: string
              responses:
                "200":
                  description: A single pet
                  content:
                    application/json:
                      schema:
                        $ref: "#/components/schemas/Pet"
                "404":
                  description: Pet not found
        components:
          schemas:
            Pet:
              type: object
              required: [id, name]
              properties:
                id:
                  type: integer
                  format: int64
                name:
                  type: string
                tag:
                  type: string
            PetList:
              type: array
              items:
                $ref: "#/components/schemas/Pet"
        """;

    public ContractStore  Store    { get; }
    public SessionManager Sessions { get; }

    public PetstoreFixture()
    {
        Store    = new ContractStore();
        Sessions = new SessionManager();
        Store.RegisterFromContent(ContractId, PetstoreYaml, format: "yaml");
    }

    /// <summary>Open a fresh session for tests that perform writes.</summary>
    public string OpenSession(string description = "test session")
    {
        var entry   = Store.Get(ContractId);
        var session = Sessions.Open(entry, description);
        return session.SessionId;
    }

    public void Dispose() { /* nothing to tear down */ }
}
