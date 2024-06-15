import os
import random
import pathlib

contents = {
    "file0.empty": 0,
    "file1.extension": 100,
    "file2.ext1.ext2.ext3": 200,
    "файл3.расширение": 300,
    "no-extension.": 400,
    ".no-name": 500
}

random.seed(12345)

root = pathlib.Path(__file__).parent.resolve()

# Remove existing files
for name, _ in contents.items():
    path = os.path.join(root, name)
    if os.path.exists(path):
        os.remove(path)
        print(f"rm {path}")
print()

# Fill the root with the test data
for name, size in contents.items():
    path = os.path.join(root, name)
    print(f"{path} -> {contents} bytes")
    with open(path, 'wb') as file:
        file.write(random.randbytes(size))
