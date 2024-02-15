import os
from itertools import chain

from helpers import get_sorted_terms, build_vector, get_idf, calculate_similarity, get_all_words

similarity_threshold = 0.05

directory_path = input("Please enter the path to the directory: ")

corpus = { }
for file_name in os.listdir(directory_path):
    if file_name.endswith('.txt'):
        file_path = os.path.join(directory_path, file_name)
        with open(file_path, 'r') as file:
            content = file.read()
        corpus[file_name] = get_all_words(content)

combined_documents = ' '.join(chain.from_iterable(corpus.values()))
terms = get_sorted_terms(combined_documents)
documents = list(corpus.values())
terms_idf = { term: get_idf(term, documents) for term in terms }
document_vectors = { index: build_vector(document, terms_idf) for index, document in corpus.items() }

while True:
    file_path = input("Please enter the path to the text file: ")

    with open(file_path, 'r') as file:
        query = file.read()

    query_vector = build_vector(get_sorted_terms(query), terms_idf)

    similarities = { index: calculate_similarity(query_vector, document_vector) for index, document_vector
                     in document_vectors.items() }
    matches = { index: value for index, value in similarities.items() if value > similarity_threshold }

    print(f'Documents in the corpus have the following similarity to the query:\n{matches}')
