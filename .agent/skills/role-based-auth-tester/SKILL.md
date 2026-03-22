---
name: role-based-auth-tester
description: Create a lightweight, robust HTML/Tailwind/JS test harness for validating role-based login logic (Admin, Teacher, Reviewer). Generates clear UI test panels, robust mock data strategies, and ensures the frontend logic is structured for a seamless future migration to Blazor .NET 10.
license: Complete terms in LICENSE.txt
---

This skill guides the creation of a distinctive, production-grade testing harness for role-based authentication systems. Implement real working test code with exceptional attention to state management, clear logging, and architectural readiness for future C# migration.

The user provides testing requirements: a login flow, role definitions, or a specific authentication edge case to test. They will specify the core roles: Site Administrator (網站管理者), Question Teacher (命題老師), and Review Expert (審題專家).

## Test Architecture Thinking

Before writing the test harness, understand the context and commit to a STRICT verification direction:
- **Purpose**: What specific login flow or routing logic is being validated? 
- **Roles**: Understand the distinct permissions and expected routing for the three core roles:
  1. `Admin` (網站管理者): System configuration, user management.
  2. `Teacher` (命題老師): Question creation, editing, submission.
  3. `Reviewer` (審題專家): Reviewing, approving, or rejecting questions.
- **Constraints**: Initially implement using HTML, Tailwind CSS, and vanilla JavaScript. Must avoid complex JS frameworks (like React/Vue) to keep the prototype lightweight before moving to Blazor .NET 10.
- **Verification**: How will the tester know the test passed? Provide clear visual feedback and detailed console logs within the UI.

**CRITICAL**: Choose a clear testing strategy. The code must perfectly simulate API latency, token retrieval, and routing redirection. The goal is to prove the business logic works, not just to make a pretty form.

Then implement the working test harness (HTML/CSS/JS) that is:
- Functional as a standalone prototype
- Visually clear (using Tailwind CSS for layout and state indicators)
- Architecturally decoupled (separating UI, data mocking, and event handling)

## Test Harness Implementation Guidelines

Focus on:
- **Mock Data Strategy**: Define clear, structured JSON objects for test accounts. Include expected tokens, role names, and target routes. This makes the JS data structure easily translatable to C# Models/DTOs later.
- **Visual Logging (Test Console)**: Always build an on-screen terminal/log output area. Use color-coded text for different log levels: Info (Blue), Success (Green), Warning (Yellow), Error (Red). Testers need to see the execution flow without opening Chrome DevTools.
- **State Management**: Clearly reflect the currently selected test role in the UI. When a role is selected, automatically populate the username and password fields to reduce manual typing.
- **Blazor Migration Readiness**: 
  - Write JavaScript functions cleanly and modularly. 
  - Treat DOM manipulation functions like Blazor `.razor` rendering.
  - Treat state and mock data like a C# ViewModel or Service layer.
  - Document where `fetch()` or `setTimeout()` will be replaced by `HttpClient` in .NET.
- **UI/UX for Testers**: Use Tailwind CSS to create a utilitarian, clean, and highly readable interface. Prioritize layout elements like grids for role buttons, disabled/readonly states for auto-filled inputs, and distinct loading states during simulated authentication.

NEVER build generic, hard-coded single-user login forms when role testing is requested. NEVER mix complex business logic directly inside HTML inline events; keep scripts separated to mirror C# Code-Behind patterns. 

**IMPORTANT**: Match the mock complexity to the real-world scenario. If testing JWT token expiry, simulate the expiry time. If testing unauthorized access, simulate an HTTP 403 response. Elegance in testing comes from covering edge cases predictably.

Remember: The test harness is the bridge between design and production. Make it robust, easy to read, and perfectly aligned with the future Blazor architecture.