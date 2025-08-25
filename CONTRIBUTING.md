# Contributing to Playwright Grid ![Logo][1]

:rocket::star::rocket: We truly appreciate your interest in contributing to our project! :rocket::star::rocket:

This document outlines the guidelines and best practices for contributing to the Playwright Grid project.
These guidelines are designed to facilitate smooth collaboration and maintain project quality. Please note that these are
recommendations rather than strict requirements, and we welcome suggestions for improvements through pull requests.


> **Important**: By submitting contributions to this project, you agree to license your work under the same terms
currently used by the project. Please be aware that the project maintainers may update the licensing terms in the future.

#### Table of Contents

* [Have a Question?](#have-a-question)
* [Ways to Contribute](#ways-to-contribute)
  * [Issue Reporting](#issue-reporting)
  * [Feature Requests](#feature-requests)
  * [Code Contributions](#code-contributions)
  * [Pull Request Process](#pull-request-process)
  * [Contribution Categories](#contribution-categories)
  * [Ready Criteria](#ready-criteria)
  * [Completion Standards](#completion-standards)
  * [Security Consideration](#-security-considerations)

## Have a Question?

Before creating an issue for your question, please try these steps first:

* Search through existing GitHub issues in this repository: https://github.com/agenixframework/agenix-playwright-grid/issues
* Review the project documentation: README (https://github.com/agenixframework/agenix-playwright-grid/blob/main/README.md) and docs/ (https://github.com/agenixframework/agenix-playwright-grid/tree/main/docs).

If you still need help, please create a new issue: https://github.com/agenixframework/agenix-playwright-grid/issues/new

## Ways to Contribute

### Issue Reporting

This section helps you submit effective bug reports. Clear reports help maintainers and the community understand,
reproduce, and resolve issues more efficiently :pencil: :computer: :mag:.

Before submitting a bug report, please review [the checklist below](#before-reporting-issues) to ensure it's necessary.
When creating a bug report, please use the GitHub issue form: https://github.com/agenixframework/agenix-playwright-grid/issues/new/choose and ensure the [ready criteria](#ready-criteria) are met. This information helps us address issues more quickly.
Always verify that the issue exists in the latest version of the project.

> **Note:** If you discover a **Closed** issue that matches your experience, please open a new issue and reference
the closed one in your description.

#### Before Reporting Issues

* Consult the project documentation to confirm the behavior isn't intentional: README (https://github.com/agenixframework/agenix-playwright-grid/blob/main/README.md) and docs/ (https://github.com/agenixframework/agenix-playwright-grid/tree/main/docs).
* Search existing issues in this repository: https://github.com/agenixframework/agenix-playwright-grid/issues. If it exists and remains open, add context to the existing issue instead of creating a duplicate.

### Feature Requests

This section guides you through suggesting improvements, new features, or maintenance tasks.
Clear suggestions help maintainers understand your needs :pencil: and identify related requests :mag:.
We categorize suggestions into different [contribution categories](#contribution-categories):
Maintenance, Enhancements, Features, and Bugs. This section covers the first three; for bug reports, see [above](#issue-reporting).

Before submitting a suggestion, search existing issues: https://github.com/agenixframework/agenix-playwright-grid/issues
When creating a feature request, please open via the issue chooser: https://github.com/agenixframework/agenix-playwright-grid/issues/new/choose and ensure the
[ready criteria](#ready-criteria) are satisfied.

### Code Contributions

We welcome contributions to any open issue. To get started, review our [contribution categories](#contribution-categories)
and their associated complexity levels.

>**Development Note**: For bug fixes, please create your branch from `main`.

We value community involvement in milestone work and encourage close collaboration with maintainers.
If you're interested in working on a milestone issue, please coordinate with the team to ensure timely completion
before the planned release date.

#### Development Environment

To contribute code, you'll need the following tools:

* **.NET 8**
  Install the .NET SDK and ensure it's properly configured in your PATH. Required for building and running tests.

* **Docker & Docker Compose**
  Required to run the full grid stack locally and for integration tests via Docker/Testcontainers.

* **IDE (recommended)**
  Visual Studio, Visual Studio Code, or JetBrains Rider for managing the solution, building, and testing.

* **Git**
  Version control system for managing code changes and collaborating with the team.

#### New to the Project?

Look for issues tagged with `good first issue` - these typically require minimal code changes and are perfect for
getting familiar with the codebase.

### 🚀 Building and Testing

#### **Local Development**

```console
# Restore packages
dotnet restore

# Build the full solution (Debug)
dotnet build PlaywrightGrid.sln -c Debug

# Run unit tests only (fast)
dotnet test WorkerService.Tests/WorkerService.Tests.csproj -c Debug

# Run all tests (units + integration GridTests via Testcontainers or local)
dotnet test PlaywrightGrid.sln -c Debug
```

#### Integration (local stack, optional)
- Start the grid locally (Hub, 2+ Workers, Dashboard, Redis, Prometheus, Grafana):
  - docker compose up --build
- In another shell (attach tests to the local grid):
```console
export GRID_TESTS_USE_LOCAL=1
export HUB_URL=http://127.0.0.1:5100
export HUB_RUNNER_SECRET=runner-secret
# Run integration tests only
dotnet test tests/GridTests.csproj -c Debug
```
Notes and env toggles (e.g., GRID_TESTS_REUSE, GRID_TESTS_FORCE_BUILD, GRID_TESTS_SKIP_CLEANUP, GRID_TESTS_HEALTH_TIMEOUT_SECONDS) are documented in README.md.

### Pull Request Process

All contributions, including those from maintainers, must go through the pull request process.
Before submitting a pull request, ensure the [completion standards](#completion-standards) are met.
Every pull request must link to an existing issue. If no issue exists, please create one first to facilitate
separate discussions about requirements (issue) and implementation (pull request).

This process helps us:
* Maintain high code quality standards
* Keep the community informed about changes
* Facilitate discussions to ensure optimal solutions
* Provide a sustainable review system for maintainers

>**Branch Note**: For bug fixes, ensure your base branch is the main development branch and targets the same for your pull request.

Our pull request workflow includes:
* Automated builds via GitHub Actions: https://github.com/agenixframework/agenix-playwright-grid/actions. Successful builds are required before review.
* Maintainer review with feedback and suggestions.
* Quality gates and code standards verification for internal contributions.

#### **Adding New Dependencies**

1. Evaluate necessity and alternatives
2. Check license compatibility
3. Add to the appropriate project only
4. Update documentation

### 📝 Coding Standards

#### **Naming Conventions**

- **Classes**: PascalCase (`CustomerService`, `HttpRequestBuilder`)
- **Methods**: PascalCase (`PerformAs`, `AnsweredBy`)
- **Properties**: PascalCase (`Name`, `RequestUrl`)
- **Fields**: camelCase with underscore prefix for private (`_httpClient`, `_connectionString`)
- **Constants**: PascalCase (`MaxRetryCount`, `DefaultTimeout`)
- **Interfaces**: PascalCase with 'I' prefix (`IPerformable`, `IQuestion<T>`)

#### **Code Style**

- **File scoped namespaces** for new files
- **Primary constructors** where appropriate (C# 12 feature)
- **Expression-bodied members** for simple implementations
- **Nullable reference types** enabled
- **XML documentation** for public APIs

### 🧪 Testing Guidelines

#### **Test Structure**

- **Arrange-Act-Assert** pattern for unit tests
- **Given-When-Then** structure for BDD scenarios
- **Descriptive test names** that explain the scenario

#### **Test Categories**

- **Unit Tests**: Fast, isolated, test single components
- **Integration Tests**: Test component interactions
- **Acceptance Tests**: End-to-end business scenarios using Screenplay pattern

#### **Branch Strategy**

1. main: Default branch
2. feature/...: Individual feature development
3. bugfix/...: Bug fixes
4. release/...: Release preparation (optional)

#### **Commit Guidelines**

- Use **conventional commits** format:
  ```
  type(scope): description

  feat(screenplay): add question chaining support
  fix(http): resolve timeout issue in HttpClient
  docs(readme): update installation instructions
  test(validation): add binary comparison tests
  ```

#### Review Standards

Code reviews focus on these key areas:

* Adherence to project coding standards and best practices
* Adequate test coverage for new functionality
* Updated documentation for changes

### Contribution Categories

We classify contributions by complexity and scope:

* **Maintenance**
  Routine updates like dependency upgrades. These vary in size but typically require minimal project knowledge
  since automated tests verify correctness.

* **Enhancements**
  Small to medium improvements to existing functionality. These can usually be completed with basic to moderate
  understanding of the project architecture.

* **Features**
  Significant additions like new integrations or major functionality. These require considerable time investment
  and deep project knowledge.

* **Bugs**
  Issues where the software doesn't behave as documented. Complexity varies depending on the specific problem.

### Ready Criteria

The "definition of ready" establishes it when a backlog item is prepared for development work.
When creating issues, please ensure these criteria are met to facilitate immediate development:

* The appropriate issue form is completed (use the chooser): https://github.com/agenixframework/agenix-playwright-grid/issues/new/choose
* Large changes are broken down into manageable tasks
* At least one maintainer understands the context and requirements
* Clear acceptance criteria are defined:
  * For bugs: A reproducible test case provided
  * For features/enhancements: User story with acceptance criteria included

### Completion Standards

The "definition of done" specifies when work is ready for review.
Please verify these criteria are met before submitting for review:

* All issue requirements have been addressed
* Tests for acceptance criteria are implemented and passing
* Comprehensive unit and integration tests are included
* Documentation has been updated to reflect changes

### 🔒 Security Considerations

- **No hardcoded credentials** in code or tests
- **Sensitive data** should use configuration or environment variables
- **Dependencies** regularly updated for security patches
- **Test data** should not contain real customer information

[1]: dashboard/wwwroot/images/logo.svg "Playwright Grid Logo"
