import os

while True:
    file_path = input("Please enter the path to the text file: ")
    directory_path = input("Please enter the path to the directory: ")

    with open(file_path, 'r') as file:
        content = file.read()
    terms = content.split(' ')

    corpus = {}
    for file_name in os.listdir(directory_path):
        if file_name.endswith('.txt'):
            file_path = os.path.join(directory_path, file_name)
            with open(file_path, 'r') as file:
                content = file.read()
            corpus[file_name] = content

    matching_documents = []
    for name, content in corpus.items():
        if any(word in content for word in terms):
            matching_documents.append(name)

    print(f'The following documents match the query: {matching_documents}')
