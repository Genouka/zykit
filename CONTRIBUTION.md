# Contributing to ZYKit

We welcome contributions from everyone. By participating in this project, you agree to abide by our Code of Conduct (if applicable) and the terms outlined in this document.

---

## How to Contribute

* Report bugs – Open an issue describing the problem, including steps to reproduce.
* Suggest enhancements – Open an issue with your idea and use case.
* Submit code – Fork the repo, create a branch, make your changes, and open a pull request (PR).
* Improve documentation – Same process as code contributions.

For major changes, please open an issue first to discuss what you would like to change.

---

## Development Setup

1. Fork and clone the repository.
2. Install dependencies (see README.md for details).
3. Run tests locally to ensure everything works:
   ```bash
   ohpm build
   ```
4. Create a new branch for your work:
   ```bash
   git checkout -b feature/your-descriptive-name
   ```

---

## Code Standards

* Follow the existing coding style (linters are configured in the repo).
* Write clear, self‑documenting code and add comments where necessary.
* Include unit tests for new functionality and ensure all tests pass.
* Update the documentation (e.g., README.md) if your changes affect usage.

---

## Commit Message Guidelines

We follow the Conventional Commits format:

```
<type>(<scope>): <subject>

[optional body]

[optional footer]
```

Example:

```
feat(parser): add support for JSON output

Closes #123
```

All commits must include a Signed-off-by line (see the Contributor Agreement below).

---

## Pull Request Process

1. Push your branch to your fork and open a PR against the main (or develop) branch.
2. Fill out the PR template with a clear description of the changes and the motivation.
3. Ensure all CI checks pass (tests, linting, etc.).
4. The PR will be reviewed by maintainers. Address any feedback.
5. Once approved, a maintainer will merge your PR.

---

## Contributor Agreement (Important)

By submitting a contribution to this project (including but not limited to code, documentation, or other materials), you agree to the following legally binding terms:

1. You warrant that your contribution is your original work, and you have the full right and authority to make this contribution.
2. You irrevocably transfer and assign all copyright, title, and interest in and to the contribution to the project owner (currently [Owner Name or Organization]). This includes the right to enforce the copyright against third parties.
3. You grant the project owner and its designees a perpetual, worldwide, royalty‑free, irrevocable, and sublicensable license to use, reproduce, modify, distribute, and otherwise exploit the contribution in any manner, including the right to relicense the entire project (including your contribution) under any license of the owner’s choice, whether open‑source or proprietary, without further notice or consent from you.
4. You acknowledge that the project is currently distributed under the Apache License, Version 2.0, but that this license may be changed at any time by the project owner, in accordance with clause 3, and you waive any right to object to such relicensing.
5. You confirm that you have added the following line to every commit that contains your contribution:
   ```
   Signed-off-by: Your Full Name <your.email@example.com>
   ```
   This line serves as your electronic signature and indicates your acceptance of this Contributor Agreement. If you cannot add this line (e.g., due to corporate policy), you must contact the maintainers to sign an alternative contributor license agreement (CLA) before your contribution can be accepted.
6. You understand that this agreement applies to all contributions you make now and in the future, unless you explicitly withdraw your consent in writing prior to submitting the contribution.

---

## Code of Conduct

We expect all participants to be respectful and constructive. Please read our Code of Conduct before engaging.

---

## Questions?

Feel free to open an issue or reach out to the maintainers at [maintainer@example.com].

Thank you for contributing!

