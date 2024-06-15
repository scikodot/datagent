import os
import random
import pathlib
import shutil

contents = {
    "Empty": {},
    "SingleLevel": {
        "file1": 100,
        "file2": 200,
        "file3": 300
    },
    "MultiLevel": {
        "folder1": {
            "subfolder1": {
                "file1_1.txt": 1100,
                "file2_1.csv": 1200
            },
            "file3": 1300
        },
        "folder2": {
            "file1_2.sfx": 2100,
            "file2_2.mp4": 2200
        },
        "folder3": {},
        "file3": 2300,
        "file4.ext": 400
    },
    "Unicode": {
        "директория1": {
            "документ1": 1100,
            "файл2": 1200
        },
        "папка2": {},
        "документ1_outer": 2100,
        "файл2_outer": 2200
    }
}

random.seed(12345)

def gen(path, contents):
    typ = type(contents)
    if typ is dict:
        print(f"{path}")
        pathlib.Path(path).mkdir(exist_ok=True)
        for name, contents in contents.items():
            gen(os.path.join(path, name), contents)
    elif typ is int:
        print(f"{path} -> {contents} bytes")
        with open(path, 'wb') as file:
            file.write(random.randbytes(contents))

root = pathlib.Path(__file__).parent.resolve()

# Indicate that the top-level directories are generated
contents = dict((f"_gen_{name}", value) for name, value in contents.items())

# Remove existing top-level directories
for name, _ in contents.items():
    path = os.path.join(root, name)
    if os.path.exists(path):
        shutil.rmtree(path)
        print(f"rm {path}")
print()    

# Fill the root with the test data
gen(root, contents)
