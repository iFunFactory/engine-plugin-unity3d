from django.shortcuts import render
from django.http import HttpResponse
from board.models import announcement
import json

BASE_ANNOUNCEMENT_COUNT = 5

def get_announcement_list(request):
    length = announcement.objects.count()
    count = min([BASE_ANNOUNCEMENT_COUNT, length])

    if 'count' in request.GET:
        count = int(request.GET.get("count"))
        if count <= 0:
            count = min([BASE_ANNOUNCEMENT_COUNT, length])
        elif count > length:
            count = length

    data = {'list': [item.ToJSON() for item in announcement.objects.order_by('-create_date')[:count]]}
    return HttpResponse(json.dumps(data), content_type='application/json')
