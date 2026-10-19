import subprocess

script = """
with open("/etc/nginx/sites-available/eseq", "r") as f:
    content = f.read()

if "map $http_upgrade $connection_upgrade" not in content:
    map_block = "map $http_upgrade $connection_upgrade {\\n    default upgrade;\\n    \\''\\'' close;\\n}\\n\\n"
    content = map_block + content

content = content.replace('proxy_set_header Connection "Upgrade";', 'proxy_set_header Connection $connection_upgrade;')
content = content.replace('proxy_set_header Connection "upgrade";', 'proxy_set_header Connection $connection_upgrade;')

with open("/etc/nginx/sites-available/eseq", "w") as f:
    f.write(content)

print("Updated eseq config")
"""

cmd = f"ssh root@10.1.97.2 'python3 -c \"{script}\"'"
result = subprocess.run(["ssh", "root@10.1.97.2", f"python3 -c '{script}'"], capture_output=True, text=True)
print("STDOUT:", result.stdout)
print("STDERR:", result.stderr)
