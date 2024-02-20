import os
import string

directory_path = input("Please enter the path to the directory: ")
corpus = {}
translator = str.maketrans('', '', string.punctuation)
for file_name in os.listdir(directory_path):
    if file_name.endswith('.txt'):
        file_path = os.path.join(directory_path, file_name)
        with open(file_path, 'r') as file:
            content = file.read()
        corpus[file_name] = set(content.translate(translator).split(' '))

while True:
    file_path = input("Please enter the path to the text file: ")

    or_terms = []
    with open(file_path, 'r') as file:
        for line in file:
            and_terms = line.strip().split(' ')
            or_terms.append(and_terms)

    matching_documents = []
    for name, content in corpus.items():
        if any(all(and_term in content for and_term in and_terms) for and_terms in or_terms):
            matching_documents.append(name)

    print(f'The following documents match the query: {matching_documents}')
