/**
 * @fileoverview Unit tests for role-based access control in the question-setting system.
 * This script simulates user logins for different roles (Admin, Teacher, Reviewer)
 * and verifies that the correct UI modules are displayed based on their permissions.
 * 
 * This test assumes that `role-login-test.html` is loaded and the `TestHarness` object
 * (defined within that HTML file) is available in the global scope.
 * 
 * To run this test, open `role-login-test.html` in a browser and paste this script
 * into the browser's developer console, or integrate it with a headless browser
 * testing framework like Playwright or Puppeteer.
 */

(async function() {
    console.log('🚀 Starting Role-Based Access Control Unit Tests...');

    // Helper function to simulate a delay
    const delay = ms => new Promise(res => setTimeout(res, ms));

    // Helper function to log test results
    const logTestResult = (testName, passed, message) => {
        const status = passed ? '✅ PASS' : '❌ FAIL';
        console.log(`${status}: ${testName} - ${message}`);
        if (!passed) {
            console.error(`Failure in ${testName}: ${message}`);
        }
    };

    // Expected permissions based on role_permissions.md
    const expectedPermissions = {
        admin: [
            'dashboard', 'projects', 'overview', 'tasks', 'reviews',
            'teachers', 'roles', 'announcements_edit'
        ],
        teacher: [
            'tasks', 'reviews', 'announcements_view'
        ],
        reviewer: [
            'reviews', 'announcements_view'
        ]
    };

    /**
     * Performs a login simulation and verifies displayed modules.
     * @param {string} roleName - The role to test (e.g., 'admin', 'teacher', 'reviewer').
     * @param {Array<string>} expectedModules - An array of module IDs expected to be visible.
     */
    async function testRoleLogin(roleName, expectedModules) {
        console.log(`\n--- Testing Role: ${roleName.toUpperCase()} ---`);
        
        // 1. Select the role
        TestHarness.selectRole(roleName);
        await delay(100); // Allow UI to update

        // 2. Simulate login
        // TestHarness.handleLogin() is an async function that simulates login and redirects.
        // For unit testing, we need to prevent the actual redirect and check the state before it.
        // The role-login-test.html's TestHarness.handleLogin() currently redirects.
        // For a true unit test, we'd mock window.location.href or call a modified login function.
        // For this demonstration, we'll assume TestHarness.handleLogin() has been modified
        // or we are checking the state *before* the redirect happens in a controlled environment.
        // Given the current `role-login-test.html`, `TestHarness.handleLogin()` performs a redirect.
        // To test the UI *after* login, we need to inspect the `moduleGrid` before the redirect.
        // The `role-login-test.html` already has a `dashboardPreview` section that dynamically updates.
        // We will trigger the login and then check the `moduleGrid` content.

        // Simulate login by directly calling the internal logic that updates the UI
        // This bypasses the actual form submission and redirect for testing purposes.
        // In a real scenario, you might mock `window.location.href` or use a testing utility
        // that loads the page and interacts with it.
        
        // For this test, we'll directly call the internal `renderModules` after setting the user mock.
        const userMock = MockRepository.users[roleName];
        if (!userMock) {
            logTestResult(`Login Simulation for ${roleName}`, false, `Mock user data not found for role: ${roleName}`);
            return;
        }

        // Simulate successful login state update
        State.currentUser = userMock;
        TestHarness.renderModules(); // Manually trigger module rendering
        await delay(200); // Allow UI to render

        // 3. Verify displayed modules
        const moduleGrid = document.getElementById('moduleGrid');
        if (!moduleGrid) {
            logTestResult(`Module Grid Check for ${roleName}`, false, 'moduleGrid element not found.');
            return;
        }

        const renderedModuleIds = Array.from(moduleGrid.children).map(child => child.dataset.moduleId);
        
        const missingModules = expectedModules.filter(id => !renderedModuleIds.includes(id));
        const unexpectedModules = renderedModuleIds.filter(id => !expectedModules.includes(id));

        const allExpectedPresent = missingModules.length === 0;
        const noUnexpectedPresent = unexpectedModules.length === 0;

        logTestResult(
            `Module Presence for ${roleName}`,
            allExpectedPresent,
            allExpectedPresent ? 
                `All expected modules found. (${expectedModules.join(', ')})` :
                `Missing modules: ${missingModules.join(', ')}`
        );

        logTestResult(
            `No Unexpected Modules for ${roleName}`,
            noUnexpectedPresent,
            noUnexpectedPresent ? 
                `No unexpected modules found.` :
                `Unexpected modules: ${unexpectedModules.join(', ')}`
        );

        // Log out for the next test
        TestHarness.handleLogout();
        await delay(100); // Allow UI to reset
    }

    // Run tests for each role
    await testRoleLogin('admin', expectedPermissions.admin);
    await testRoleLogin('teacher', expectedPermissions.teacher);
    await testRoleLogin('reviewer', expectedPermissions.reviewer);

    console.log('\n🎉 Role-Based Access Control Unit Tests Completed.');
})();
