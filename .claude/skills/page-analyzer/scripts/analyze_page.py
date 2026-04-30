
import os
import re
from datetime import datetime

def analyze_page_content(file_path):
    """Simulates analyzing page content to extract information."""
    tech_stack = []
    page_structure = []
    tables_used = []
    sql_queries = []
    other_info = []

    with open(file_path, 'r', encoding='utf-8') as f:
        content = f.read()

    # Dummy analysis - in a real scenario, this would involve more sophisticated parsing
    if '.razor' in file_path or '.cshtml' in file_path:
        tech_stack.append('.NET Blazor/Razor Pages')
        if re.search(r'@inject\s+\w+\s+Service', content):
            tech_stack.append('Dependency Injection')
        if re.search(r'Dapper', content):
            tech_stack.append('Dapper ORM')
        if re.search(r'<form', content):
            page_structure.append('表單元件')
        if re.search(r'Login', content, re.IGNORECASE):
            page_structure.append('登入相關元件')
        if re.search(r'Users', content):
            tables_used.append('Users')
        if re.search(r'LoginLogs', content):
            tables_used.append('LoginLogs')
        sql_matches = re.findall(r'(SELECT.*?FROM.*?WHERE.*?)|(INSERT INTO.*?VALUES.*?)|(UPDATE.*?SET.*?WHERE.*?)|(DELETE FROM.*?WHERE.*?)', content, re.IGNORECASE | re.DOTALL)
        for match_tuple in sql_matches:
            for match in match_tuple:
                if match:
                    sql_queries.append(match.strip())

    elif '.html' in file_path or '.vue' in file_path or '.js' in file_path:
        tech_stack.append('Frontend Framework (e.g., Vue.js, React, plain HTML/JS)')
        if re.search(r'axios|fetch', content):
            tech_stack.append('AJAX/Fetch API')
        if re.search(r'<div id="app"', content):
            page_structure.append('Vue.js 根元件')
        if re.search(r'LoginComponent', content):
            page_structure.append('登入元件')
        if re.search(r'users_table', content):
            tables_used.append('users_table (推測後端API使用)')

    # General checks
    if re.search(r'Bootstrap', content):
        tech_stack.append('Bootstrap CSS Framework')
    if re.search(r'jQuery', content):
        tech_stack.append('jQuery')

    return {
        'TechStack': '\n- ' + '\n- '.join(sorted(list(set(tech_stack)))) if tech_stack else 'N/A',
        'PageStructure': '\n- ' + '\n- '.join(sorted(list(set(page_structure)))) if page_structure else 'N/A',
        'TablesUsed': '\n- ' + '\n- '.join(sorted(list(set(tables_used)))) if tables_used else 'N/A',
        'SQLQueries': '\n```sql\n' + '\n'.join(sorted(list(set(sql_queries)))) + '\n```' if sql_queries else 'N/A',
        'OtherInfo': 'N/A'
    }

def generate_page_doc(project_path, output_dir):
    """Generates documentation for each web page found in the project path."""
    if not os.path.exists(output_dir):
        os.makedirs(output_dir)

    template_path = '/home/ubuntu/skills/page-analyzer/templates/page_doc_template.md'
    with open(template_path, 'r', encoding='utf-8') as f:
        template = f.read()

    # Supported file extensions
    page_extensions = ('.razor', '.cshtml', '.html', '.vue', '.js', '.aspx', '.ascx')

    found_pages = []
    for root, _, files in os.walk(project_path):
        for file in files:
            if file.endswith(page_extensions):
                found_pages.append(os.path.join(root, file))

    if not found_pages:
        print(f"在路徑 '{project_path}' 中沒有找到任何網頁檔案 ({', '.join(page_extensions)})。")
        return

    for page_file_path in found_pages:
        page_name_with_ext = os.path.basename(page_file_path)
        page_name = os.path.splitext(page_name_with_ext)[0]
        page_title = page_name.replace('_', ' ').replace('-', ' ').title() # Simple title generation

        analysis_results = analyze_page_content(page_file_path)

        # Placeholder for SiteDescription and DesignConsiderations - these would typically come from a higher-level analysis or user input
        site_description = "此網站最重要的功能為命題任務與審題任務，旨在提供所有命題教師與審題專家方便的命審工具。所有資訊根據專案梯次進行分類，且提供管理員儀表板進行專案梯次資訊的監管"
        design_considerations = "使用者多為年長者、須注重功能與效能"

        formatted_content = template.format(
            PageName=page_name,
            PageTitle=page_title,
            CurrentDate=datetime.now().strftime('%Y-%m-%d'),
            SiteDescription=site_description,
            DesignConsiderations=design_considerations,
            **analysis_results
        )

        output_file_path = os.path.join(output_dir, f"{page_name}.md")
        with open(output_file_path, 'w', encoding='utf-8') as f:
            f.write(formatted_content)
        print(f"已生成文件: {output_file_path}")

if __name__ == '__main__':
    # This script will be called by the agent, which will provide the project_path and output_dir.
    # For testing purposes, you can uncomment and modify these lines:
    # dummy_project_path = '/home/ubuntu/dummy_project'
    # dummy_output_dir = '/home/ubuntu/MTrefer/pageFinal_doc'
    # # Create dummy project for testing
    # os.makedirs(dummy_project_path, exist_ok=True)
    # with open(os.path.join(dummy_project_path, 'Login.razor'), 'w') as f:
    #     f.write('''
    # @page "/login"
    # @inject AuthService Service
    # <form>
    #     <input type="email" />
    #     <input type="password" />
    #     <button type="submit">Login</button>
    # </form>
    # @code {
    #     public string Email { get; set; }
    #     public string Password { get; set; }
    #     public async Task HandleLogin() {
    #         // Dapper call example
    #         var user = await Service.QuerySingleOrDefaultAsync<User>("SELECT * FROM Users WHERE Email = @Email AND Password = @Password", new { Email, Password });
    #         // Insert log
    #         await Service.ExecuteAsync("INSERT INTO LoginLogs (UserId, LoginTime) VALUES (@UserId, GETDATE())", new { user.Id });
    #     }
    # }
    # ''')
    # with open(os.path.join(dummy_project_path, 'Home.html'), 'w') as f:
    #     f.write('''
    # <!DOCTYPE html>
    # <html>
    # <head><title>Home Page</title></head>
    # <body>
    #     <div id="app">
    #         <h1>Welcome Home</h1>
    #         <p>This is the home page content.</p>
    #     </div>
    #     <script src="app.js"></script>
    # </body>
    # </html>
    # ''')
    # with open(os.path.join(dummy_project_path, 'app.js'), 'w') as f:
    #     f.write('''
    # console.log("App started");
    # fetch('/api/data').then(res => res.json()).then(data => console.log(data));
    # ''')
    # generate_page_doc(dummy_project_path, dummy_output_dir)

    # In actual execution, the agent will pass arguments via command line
    import argparse
    parser = argparse.ArgumentParser(description='Generate documentation for web pages.')
    parser.add_argument('--project_path', required=True, help='Path to the web project directory.')
    parser.add_argument('--output_dir', required=True, help='Directory to save the generated markdown files.')
    args = parser.parse_args()

    generate_page_doc(args.project_path, args.output_dir)
