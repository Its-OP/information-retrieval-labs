import math
import string
from typing import Dict, Set, List


def get_all_words(document: str) -> List[str]:
    translator = str.maketrans('', '', string.punctuation)
    no_punctuation_string = document.translate(translator)
    return no_punctuation_string.split()


def get_sorted_terms(document: str) -> Set[str]:
    words = get_all_words(document)
    terms = set(sorted(set(words)))
    return terms


def build_vector(document: List[str], terms_idf: Dict[str, float]) -> List[float]:
    vector = [0] * len(terms_idf)

    i = 0
    for term, weight in terms_idf.items():
        if term not in document:
            vector[i] = 0
        else:
            count_occurrences = sum(1 for word in document if term == word)
            tf = count_occurrences / len(document)
            vector[i] = tf * terms_idf[term]
        i += 1
    return vector


def get_idf(term: str, documents: List[List[str]]) -> float:
    count_doc_with_term = sum(1 for document in documents if term in document)
    numerator = len(documents) - count_doc_with_term if len(documents) > count_doc_with_term else 0.00001
    return math.log(numerator / count_doc_with_term)


def calculate_similarity(query_vector: List[float], document_vector: List[float]) -> float:
    def get_abs_value(vector: List[float]):
        return math.sqrt(sum(weight ** 2 for weight in vector))

    numerator = sum(query_vector[i] * document_vector[i] for i in range(len(query_vector)))
    denominator = get_abs_value(query_vector) * get_abs_value(document_vector)
    return numerator / denominator
